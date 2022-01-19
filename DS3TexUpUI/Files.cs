using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

#nullable enable

namespace DS3TexUpUI
{
    public static class Files
    {
        public static void InvertDirectoryStructure(string source, string target)
        {
            InvertDirectoryStructure(new NoopProgressToken(), source, target);
        }
        public static void InvertDirectoryStructure(IProgressToken token, string source, string target)
        {
            var inv = GetInvertedDirectoryStructure(source);

            token.ForAll(inv, pair =>
            {
                var (from, toRelative) = pair;
                var to = Path.Join(target, toRelative);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(from, to, true);
            });
        }
        private static List<(string from, string toRelative)> GetInvertedDirectoryStructure(string source)
        {
            var level1 = Directory.GetDirectories(source).Select(Path.GetFileName).ToArray();

            var level2 = new HashSet<string>();
            foreach (var l1 in level1)
            {
                level2.UnionWith(Directory.GetDirectories(Path.Join(source, l1)).Select(f => Path.GetFileName(f)));
            }

            var result = new List<(string, string)>();
            foreach (var l1 in level1)
            {
                foreach (var l2 in level2)
                {
                    var d = Path.Join(source, l1, l2);
                    if (Directory.Exists(d))
                    {
                        foreach (var file in Directory.GetFiles(d))
                        {
                            result.Add((file, Path.Join(l2, l1, Path.GetFileName(file))));
                        }
                    }
                }
            }
            return result;
        }

        public static void CopyFilesRecursively(IProgressToken token, DirectoryInfo source, DirectoryInfo target)
        {
            token.CheckCanceled();

            foreach (DirectoryInfo dir in source.GetDirectories())
            {
                CopyFilesRecursively(token, dir, target.CreateSubdirectory(dir.Name));
            }

            foreach (FileInfo file in source.GetFiles())
            {
                token.CheckCanceled();
                file.CopyTo(Path.Combine(target.FullName, file.Name));
            }
        }

        public static void RemoveFilesIf(IProgressToken token, DirectoryInfo root, Func<FileInfo, bool> condition)
        {
            token.CheckCanceled();

            foreach (DirectoryInfo dir in root.GetDirectories())
            {
                RemoveFilesIf(token, dir, condition);
            }

            foreach (FileInfo file in root.GetFiles())
            {
                token.CheckCanceled();
                if (condition(file)) file.Delete();
            }
        }

        private static string GetOutputFile(this TexId id, string outputDir)
        {
            var file = Path.Join(outputDir, id.Category, $"{id.Name.ToString()}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            return file;
        }
        private static void RemoveUnchanged(IReadOnlyCollection<Dictionary<TexId, string>> files, Func<string, bool>? didChange)
        {
            if (didChange == null) return;

            var changed = new HashSet<TexId>();
            var all = new HashSet<TexId>();
            foreach (var d in files)
            {
                foreach (var (id, file) in d)
                {
                    all.Add(id);
                    if (didChange(file))
                        changed.Add(id);
                }
            }

            all.ExceptWith(changed);
            var unchanged = all;

            foreach (var d in files)
                foreach (var id in unchanged)
                    d.Remove(id);
        }

        public static void PickSharpest(IProgressToken token, IEnumerable<string> inputDirs, string outputDir, Func<string, bool>? didChange = null)
        {
            token.SubmitStatus("Searching for files");
            var files = inputDirs.SelectMany(d => Directory.GetFiles(d, "*.png", SearchOption.AllDirectories)).ToList();
            var byId = files.GroupBy(TexId.FromPath)
                .Select(g => (g.Key, g.ToList()))
                .Where(g => didChange == null || g.Item2.Any(didChange))
                .ToDictionary(g => g.Key, g => g.Item2);
            var fileCount = byId.Select(kv => kv.Value.Count).Sum();

            token.SubmitStatus($"Selecting the sharpest of {byId.Count} textures and {fileCount} files");
            token.ForAllParallel(byId, kv =>
            {
                var (id, files) = kv;

                static string SelectSharpest(List<string> files)
                {
                    if (files.Count == 1) return files[0];

                    var best = files[0];
                    var bestScore = best.LoadTextureMap().GetSharpnessScore();
                    for (int i = 1; i < files.Count; i++)
                    {
                        var f = files[i];
                        var score = f.LoadTextureMap().GetSharpnessScore();
                        if (score > bestScore) (best, bestScore) = (f, score);
                    }

                    return best;
                }

                File.Copy(SelectSharpest(files), id.GetOutputFile(outputDir));
            });
        }
        public static void CombineGroundAndMoss(IProgressToken token, TexOverrideList inputGround, TexOverrideList inputMoss, string outputDir, Func<string, bool>? didChange = null)
        {
            token.SubmitStatus("Searching for files");
            var groundFiles = inputGround.GetFiles();
            var mossFiles = inputMoss.GetFiles();
            RemoveUnchanged(new[] { groundFiles, mossFiles }, didChange);

            var withMoss = DS3.GroundWithMossTextures.ToHashSet();
            withMoss.IntersectWith(groundFiles.Keys);
            withMoss.IntersectWith(mossFiles.Keys);

            token.SubmitStatus($"Computing the combined textures of {withMoss.Count * 2} files");
            token.ForAllParallel(withMoss, id =>
            {
                var ground = groundFiles[id].LoadTextureMap();
                var moss = mossFiles[id].LoadTextureMap();
                ground.AddSmoothGreen(moss);
                ground.SaveAsPng(id.GetOutputFile(outputDir));
            });
        }
        // The inputDirs is an ordered list. If the same texture is present is multiple directories, the one from the
        // last directory is chosen.
        public static void PickAlbedoForNormals(IProgressToken token, TexOverrideList inputDirs, string outputDir, Func<string, bool>? didChange = null)
        {
            token.SubmitStatus("Searching for files");
            var relevant = DS3.NormalAlbedo.Values.ToHashSet();
            var files = new Dictionary<TexId, string>(inputDirs.GetFiles().Where(kv => relevant.Contains(kv.Key)));
            RemoveUnchanged(new[] { files }, didChange);

            token.SubmitStatus($"Copying {files.Count} files into output directory");
            token.ForAllParallel(files, kv =>
            {
                var (id, file) = kv;
                File.Copy(file, id.GetOutputFile(outputDir));
            });
        }

        public static void CombineNormalParts(IProgressToken token, TexOverrideList normal, TexOverrideList normalAlbedo, TexOverrideList gloss, TexOverrideList height, string outputDir, Func<string, bool>? didChange = null)
        {
            token.SubmitStatus("Searching for files");
            var normalFiles = normal.GetFiles();
            var glossFiles = gloss.GetFiles();
            var heightFiles = height.GetFiles();
            var albedoNormal = DS3.NormalAlbedo
                .GroupBy(kv => kv.Value)
                .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());
            var normalAlbedoFiles = normalAlbedo.GetFiles()
                .SelectMany(kv => albedoNormal.GetOrAdd(kv.Key).Select(n => (n, kv.Value)))
                .ToDictionary(p => p.n, p => p.Value);
            RemoveUnchanged(new[] { normalFiles, normalAlbedoFiles, glossFiles, heightFiles }, didChange);
            var active = normalFiles.Keys.Intersect(glossFiles.Keys).ToHashSet();

            token.SubmitStatus($"Combining {active.Count} normal textures");
            token.ForAllParallel(active, id =>
            {
                var n = DS3NormalMap.Load(normalFiles[id]);

                var normalized = false;
                if (normalAlbedoFiles.TryGetValue(id, out var naFile))
                {
                    // Combine with albedo normals
                    ITextureMap<Normal> na = DS3NormalMap.Load(naFile).Normals;
                    if (na.Width > n.Width) na = na.DownSample(Average.Normal, na.Width / n.Width);
                    if (na.Width * 2 == n.Width) na = na.UpSampleNormals(2);

                    if (na.Width == n.Width && na.Height == n.Height)
                    {
                        n.Normals.CombineWith(na, 1f);
                        normalized = true;
                    }
                    else
                    {
                        token.SubmitLog($"The sizes of n:{n.Width}x{n.Height}:{normalFiles[id]} and a:{na.Width}x{na.Height}:{naFile} are not compatible");
                    }
                }

                // The upscaled normals might not be normalized
                if (!normalized) n.Normals.Normalize();

                // Set gloss map
                var g = glossFiles[id].LoadTextureMap();
                n.Gloss.Set(g.GreyBrightness());

                if (heightFiles.TryGetValue(id, out var hFile))
                {
                    // Set height map
                    var h = heightFiles[id].LoadTextureMap();
                    n.Heights.Set(h.GreyAverage());
                }

                n.SaveAsPng(id.GetOutputFile(outputDir));
            });
        }
    }

    public class TexOverrideList : IEnumerable<string>
    {
        public readonly List<string> Directories = new List<string>();

        public TexOverrideList() { }
        public TexOverrideList(string baseDirectory) => Directories.Add(baseDirectory);
        public TexOverrideList(IEnumerable<string> collection) => Directories.AddRange(collection);

        public void Add(string directory) => Directories.Add(directory);

        public Dictionary<TexId, string> GetFiles()
        {
            return Directories
                .SelectMany(d =>
                {
                    return Directory
                        .GetFiles(d, "*.png", SearchOption.AllDirectories)
                        .Select(f => (TexId.FromPath(f), f))
                        .ToList();
                })
                .GroupBy(kv => kv.Item1)
                .ToDictionary(g => g.Key, g => g.Last().f);
        }

        public IEnumerator<string> GetEnumerator()
        {
            return ((IEnumerable<string>)Directories).GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)Directories).GetEnumerator();
        }

        public static implicit operator TexOverrideList(string baseDir) => new TexOverrideList(baseDir);
    }
}
