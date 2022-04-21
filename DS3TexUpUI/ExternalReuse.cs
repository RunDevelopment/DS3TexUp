using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using SoulsFormats;
using SixLabors.ImageSharp;
using Pfim;
using SixLabors.ImageSharp.PixelFormats;
using System.Numerics;

#nullable enable

namespace DS3TexUpUI
{
    public class ExternalReuse
    {
        public string CertainFile { get; set; } = "";
        public string UncertainFile { get; set; } = "";
        public string RejectedFile { get; set; } = "";

        public string ExternalDir { get; set; } = "";
        public IReadOnlyDictionary<string, Size> ExternalSize { get; set; } = new Dictionary<string, Size>();

        public Func<TexId, bool> Ds3Filter { get; set; } = id => true;
        public Func<string, bool> ExternalFilter { get; set; } = file => true;

        public bool RequireGreater { get; set; } = false;
        public bool SameKind { get; set; } = false;
        public Func<SizeRatio, IImageHasher>? CopyHasherFactory { get; set; } = null;
        public Func<ArrayTextureMap<Rgba32>, int> CopySpread { get; set; } = image => 2;
        public Rgba32 MaxDiff { get; set; } = default;
        public Action<ArrayTextureMap<Rgba32>>? ModifyImage { get; set; } = null;

        private Lazy<Dictionary<string, string>> ExternalFiles;

        public ExternalReuse()
        {
            ExternalFiles = new Lazy<Dictionary<string, string>>(() =>
            {
                using var md5 = System.Security.Cryptography.MD5.Create();

                return Directory.GetFiles(ExternalDir, "*.dds", SearchOption.AllDirectories).ToDictionary(f => f, f =>
                {
                    var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(f));
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                });
            });
        }

        private string GetUncertainFileName(string file)
        {
            var hash = ExternalFiles.Value[file];
            return $"{hash}@{GetExternalSize(file).Width}px.png";
        }
        private string ParseUncertainFileName(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var i = name.IndexOf('@');
            if (i != -1) name = name.Substring(0, i);

            return ExternalFiles.Value.Where(kv => kv.Value == name).Single().Key;
        }

        private static string GetUncertainDir(Workspace w) => Path.Join(w.TextureDir, "uncertain");

        private Dictionary<TexId, HashSet<string>> LoadCertain()
        {
            return CertainFile.LoadJsonFile<Dictionary<TexId, HashSet<string>>>();
        }
        private Dictionary<TexId, HashSet<string>> LoadUncertain()
        {
            return Load<Dictionary<TexId, HashSet<string>>>(UncertainFile);
        }
        private Dictionary<TexId, HashSet<string>> LoadRejected()
        {
            return Load<Dictionary<TexId, HashSet<string>>>(RejectedFile);
        }
        private static T Load<T>(string file)
            where T : new()
        {
            if (!File.Exists(file)) return new T();
            return file.LoadJsonFile<T>();
        }

        private Size GetExternalSize(string file) => ExternalSize.GetOrDefault(file, Size.Empty);

        public Action<SubProgressToken> CreateCopyIndex(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Searching for files");
                var files = ExternalFiles.Value.Keys.Where(ExternalFilter).ToArray();

                var index = CopyIndex.Create(token.Reserve(0.5), files, CopyHasherFactory);

                var copies = new Dictionary<TexId, List<string>>();

                token.SubmitStatus($"Looking up files");
                token.ForAllParallel(DS3.OriginalSize.Keys.Where(Ds3Filter), id =>
                {
                    var width = DS3.OriginalSize[id].Width;
                    var set = new HashSet<string>();

                    try
                    {
                        var image = w.GetExtractPath(id).LoadTextureMap();
                        var similar = index.GetSimilar(image, (byte)CopySpread(image));
                        if (similar != null)
                        {
                            set.UnionWith(similar.Where(e => RequireGreater ? e.Width > width : e.Width >= width).Select(e => e.File));
                        }
                    }
                    catch (System.Exception e)
                    {
                        lock (token.Lock)
                        {
                            token.SubmitLog($"Ignoring {id} due to error");
                            token.LogException(e);
                        }
                    }

                    if (SameKind)
                    {
                        var kind = id.GetTexKind();
                        set.RemoveWhere(f => TexId.GuessTexKind(Path.GetFileNameWithoutExtension(f)) != kind);
                    }

                    var list = set.ToList();
                    list.Sort();

                    if (list.Count > 0)
                    {
                        lock (copies)
                        {
                            copies[id] = list;
                        }
                    }
                });

                token.SubmitStatus("Saving JSON");
                copies.SaveAsJson(UncertainFile);
            };
        }

        public Action<SubProgressToken> CreateIdentical(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Finding identical");
                var identical = new Dictionary<TexId, List<string>>();

                bool AreIdentical(ArrayTextureMap<Rgba32> imageA, ArrayTextureMap<Rgba32> imageB)
                {
                    var maxDiff = MaxDiff;
                    for (int i = 0; i < imageA.Count; i++)
                    {
                        var pa = imageA[i];
                        var pb = imageB[i];

                        var diffR = Math.Abs(imageA[i].R - imageB[i].R);
                        var diffG = Math.Abs(imageA[i].G - imageB[i].G);
                        var diffB = Math.Abs(imageA[i].B - imageB[i].B);
                        var diffA = Math.Abs(imageA[i].A - imageB[i].A);

                        if (diffR > maxDiff.R || diffG > maxDiff.G || diffB > maxDiff.B || diffA > maxDiff.A)
                            return false;
                    }
                    return true;
                }

                token.ForAllParallel(LoadUncertain(), entry =>
                {
                    var (id, copies) = entry;
                    var idSize = DS3.OriginalSize[id];
                    var image = new Lazy<ArrayTextureMap<Rgba32>>(() => w.GetExtractPath(id).LoadTextureMap());

                    var list = new List<string>();
                    foreach (var file in copies)
                    {
                        var fileSize = GetExternalSize(file);
                        if (fileSize == idSize && AreIdentical(file.LoadTextureMap(), image.Value))
                        {
                            list.Add(file);
                        }
                    }

                    list.Sort();
                    if (list.Count > 0)
                    {
                        lock (identical)
                        {
                            identical[id] = list;
                        }
                    }
                });

                token.SubmitStatus("Saving JSON");
                identical.SaveAsJson(CertainFile);
            };
        }

        public Action<SubProgressToken> CreateUncertainDirectory(Workspace w)
        {
            return token =>
            {
                var uncertainDir = GetUncertainDir(w);

                var certain = LoadCertain();
                var uncertain = LoadUncertain();
                var rejected = LoadRejected();

                token.SubmitStatus("Copying files");
                token.ForAllParallel(uncertain, pair =>
                {
                    var (id, copies) = pair;

                    var certainFiles = certain.GetOrNew(id);
                    var rejectedFiles = rejected.GetOrNew(id);

                    // Remove duplicates and rejected
                    var equal = copies.Except(rejectedFiles).Except(certainFiles).ToList();

                    if (equal.Count == 0) return;

                    if (equal.Count > 25)
                    {
                        token.SubmitLog($"Too many copies ({equal.Count}) for {id}.");
                        return;
                    }

                    var dir = Path.Join(uncertainDir, id.Category, id.Name);
                    Directory.CreateDirectory(dir);

                    var maxWidth = equal.Select(f => GetExternalSize(f).Width).Append(DS3.OriginalSize[id].Width).Max();

                    {
                        var idImage = w.GetExtractPath(id).LoadTextureMap();
                        var target = Path.Join(dir, $"_@{idImage.Width}px.png");
                        ModifyImage?.Invoke(idImage);
                        if (idImage.Width < maxWidth) idImage = idImage.UpSample(maxWidth / idImage.Width, BiCubic.Rgba);
                        idImage.SaveAsPng(target);
                    }

                    foreach (var f in equal)
                    {
                        token.CheckCanceled();

                        var image = f.LoadTextureMap();
                        ModifyImage?.Invoke(image);
                        if (image.Width < maxWidth) image = image.UpSample(maxWidth / image.Width, BiCubic.Rgba);
                        image.SaveAsPng(Path.Join(dir, GetUncertainFileName(f)));
                    }
                });
            };
        }
        public Action<SubProgressToken> ReadUncertainDirectory(Workspace w)
        {
            return token =>
            {
                var uncertainDir = GetUncertainDir(w);

                var files = Directory
                    .GetFiles(uncertainDir, "*", SearchOption.AllDirectories)
                    .Where(f => !Path.GetFileName(f).StartsWith("_"))
                    .ToArray();

                static TexId GetTexIdFromDirectories(string file)
                {
                    var p = Path.GetDirectoryName(file);
                    var name = Path.GetFileName(p);
                    p = Path.GetDirectoryName(p);
                    var category = Path.GetFileName(p);
                    return new TexId(category, name);
                }

                var oldCertain = LoadCertain();
                var newCertain = new Dictionary<TexId, List<string>>();
                foreach (var group in files.GroupBy(GetTexIdFromDirectories))
                {
                    var id = group.Key;
                    var copies = group.Select(ParseUncertainFileName).Union(oldCertain.GetOrNew(id)).ToHashSet().ToList();
                    copies.Sort();
                    newCertain[id] = copies;
                }
                foreach (var id in oldCertain.Keys.Except(newCertain.Keys).ToList())
                {
                    var l = oldCertain[id].ToList();
                    l.Sort();
                    newCertain[id] = l;
                }
                newCertain.SaveAsJson(CertainFile);

                var rejected = new Dictionary<TexId, List<string>>();
                foreach (var (id, copies) in LoadUncertain())
                {
                    var r = copies.Except(newCertain.GetOrNew(id)).ToList();
                    r.Sort();
                    if (r.Count > 0)
                    {
                        rejected[id] = r;
                    }
                }
                rejected.SaveAsJson(RejectedFile);
            };
        }

        public Action<SubProgressToken> UpdateRejected()
        {
            return token =>
            {
                var certain = LoadCertain();
                var rejected = new Dictionary<TexId, List<string>>();
                foreach (var (id, copies) in LoadUncertain())
                {
                    var r = copies.Except(certain.GetOrNew(id)).ToList();
                    r.Sort();
                    if (r.Count > 0)
                    {
                        rejected[id] = r;
                    }
                }
                rejected.SaveAsJson(RejectedFile);
            };
        }

        public Action<SubProgressToken> ManuallyMakeEqual(EquivalenceCollection<TexId> certain)
        {
            return token =>
            {
                // certain.Set(LoadCertain());
                // certain.SaveAsJson(CertainFile);

                // var rejected = DifferenceCollection<TexId>.FromUncertain(LoadUncertain(), certain);
                // rejected.SaveAsJson(RejectedFile);
            };
        }
    }
}
