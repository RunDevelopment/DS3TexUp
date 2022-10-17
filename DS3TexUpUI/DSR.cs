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

#nullable enable

namespace DS3TexUpUI
{
    internal static class DSR
    {
        public const string ExtractDir = @"C:\DS3TexUp\extract-dsr";

        internal static void RemoveSolidColor(IProgressToken token)
        {
            token.SubmitStatus("Removing solid color images");

            var dds = Directory.GetFiles(ExtractDir, "*.dds", SearchOption.AllDirectories);

            token.ForAllParallel(dds, file =>
            {
                try
                {
                    using var image = DDSImage.Load(file);
                    var diff = image.GetMaxAbsDiff();
                    if (diff.R <= 10 && diff.G <= 10 && diff.B <= 10 && diff.A <= 10)
                    {
                        File.Delete(file);
                    }
                }
                catch (System.Exception e)
                {
                    token.LogException(e);
                }
            });
        }
        internal static Action<IProgressToken> Extract(string gameDir)
        {
            return token =>
            {
                token.SubmitStatus("Yabber");
                var yabber1 = new[] {
                    Directory.GetFiles(Path.Join(gameDir, "chr"), "*.chrbnd.dcx"),
                    Directory.GetFiles(Path.Join(gameDir, "map"), "*.tpfbhd", SearchOption.AllDirectories),
                    Directory.GetFiles(Path.Join(gameDir, "obj"), "*.objbnd.dcx"),
                    Directory.GetFiles(Path.Join(gameDir, "parts"), "*.partsbnd.dcx"),
                    Directory.GetFiles(Path.Join(gameDir, "sfx"), "*.ffxbnd.dcx"),
                };
                Yabber.RunParallel(token, yabber1.SelectMany(l => l).ToArray());

                // chrs are super annoying
                var chrtpfbdt = Directory.GetFiles(Path.Join(gameDir, "chr"), "*.chrtpfbdt");
                token.ForAllParallel(chrtpfbdt, bdt =>
                {
                    var c = Path.GetFileNameWithoutExtension(bdt);
                    var bhd = Path.Join(gameDir, "chr", $"{c}-chrbnd-dcx", $"chr", $"{c}", $"{c}.chrtpfbhd");
                    File.Copy(bhd, Path.Join(gameDir, "chr", $"{c}.chrtpfbhd"), overwrite: true);
                });
                Yabber.RunParallel(token, Directory.GetFiles(Path.Join(gameDir, "chr"), "*.chrtpfbhd"));

                var yabber3 = new[] { "chr", "map", "obj", "parts", "sfx" }.SelectMany(cat =>
                {
                    var dir = Path.Join(gameDir, cat);
                    return Directory.GetFiles(dir, "*.tpf", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(dir, "*.tpf.dcx", SearchOption.AllDirectories));
                });
                Yabber.RunParallel(token, yabber3.ToArray());

                void ExtractDDS(string category, bool overwrite, Func<string, string?> getName)
                {
                    token.SubmitStatus($"Extract {category}");
                    var dds = Directory.GetFiles(Path.Join(gameDir, category), "*.dds", SearchOption.AllDirectories);
                    var targetDir = Path.Join(ExtractDir, category);
                    Directory.CreateDirectory(targetDir);
                    token.ForAll(dds, file =>
                    {
                        var name = getName(file);
                        if (name == null) return;
                        var target = Path.Join(targetDir, $"{name}.dds");
                        File.Copy(file, target, overwrite: overwrite);
                    });
                }

                ExtractDDS("chr", false, file =>
                {
                    var n = Path.GetFileNameWithoutExtension(file);
                    var p = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(file)));
                    return $"{p}-{n}";
                });
                ExtractDDS("map", true, file =>
                {
                    var n = Path.GetFileNameWithoutExtension(file);
                    if (
                        n.StartsWith("GI_") ||
                        n.StartsWith("Env") ||
                        n.EndsWith("_L") ||
                        n.Contains("_lit_B") ||
                        n.Contains("_lit_b") ||
                        n == "dummy"
                    )
                    {
                        return null;
                    }
                    return n;
                });
                ExtractDDS("obj", false, file =>
                {
                    var n = Path.GetFileNameWithoutExtension(file);
                    var p = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(file)));
                    return $"{p}-{n}";
                });
                ExtractDDS("parts", false, file =>
                {
                    if (file.Contains("_M-partsbnd-dcx") || file.Contains("_L-tpf")) return null;
                    var n = Path.GetFileNameWithoutExtension(file);
                    var p = Path.GetFileName(Path.GetDirectoryName(file))!;
                    p = p.Substring(0, p.Length - "-tpf".Length);
                    return $"{p}-{n}";
                });
                ExtractDDS(category: "sfx", false, file =>
                {
                    var n = Path.GetFileNameWithoutExtension(file);
                    var p = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(file)))))!;
                    p = p.Substring("FRPG_SfxBnd_".Length);
                    p = p.Substring(0, p.Length - "-ffxbnd-dcx".Length);
                    return $"{p}-{n}";
                });

                RemoveSolidColor(token);
            };
        }

        public static IEnumerable<FlverMaterialInfo> ReadAllFlverMaterialInfo()
        {
            foreach (var file in Directory.GetFiles(Data.File(name: @"dsr/materials"), "*.json"))
                foreach (var item in file.LoadJsonFile<List<FlverMaterialInfo>>())
                    yield return item;
        }
        internal static Action<IProgressToken> ExtractFlverFiles(string gameDir)
        {
            return token =>
            {
                var files = Directory.GetFiles(gameDir, "*.flver", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(gameDir, "*.flver.dcx", SearchOption.AllDirectories));

                var results = new ConcurrentBag<FlverMaterialInfo>();
                token.ForAllParallel(files, file =>
                {
                    try
                    {
                        var f = FLVER2.Read(file);
                        results.Add(new FlverMaterialInfo()
                        {
                            FlverPath = Path.GetRelativePath(gameDir, file),
                            GXLists = f.GXLists,
                            Materials = f.Materials,
                        });
                    }
                    catch (System.Exception e)
                    {
                        token.LogException(e);
                    }
                });

                var grouped = results
                    .GroupBy(f =>
                    {
                        var end = f.FlverPath.IndexOfAny(new[] { '\\', '/' });
                        var name = f.FlverPath.Substring(0, end);
                        return name;
                    })
                    .ToDictionary(g => g.Key, g => g.ToList());
                foreach (var (name, list) in grouped)
                {
                    list.Sort((a, b) => a.FlverPath.CompareTo(b.FlverPath));
                    list.SaveAsJson(Data.File(@$"dsr/materials/{name}.json", Data.Source.Local));
                }
            };
        }

        public static IReadOnlyDictionary<string, TransparencyKind> Alpha
            = Data.File(@"dsr/alpha.json").LoadJsonFile<Dictionary<string, TransparencyKind>>();
        public static void CreateAlpha(IProgressToken token)
        {
            token.SubmitStatus("Alpha");

            var dds = Directory.GetFiles(ExtractDir, "*.dds", SearchOption.AllDirectories);

            var alpha = new Dictionary<string, TransparencyKind>();
            token.ForAllParallel(dds, file =>
            {
                try
                {
                    using var image = DDSImage.Load(file);
                    var t = image.GetTransparency();
                    lock (alpha) { alpha[file] = t; }
                }
                catch (System.Exception e)
                {
                    token.LogException(e);
                }
            });

            alpha.SaveAsJson(Data.File(@"dsr/alpha.json", Data.Source.Local));
        }

        public static IReadOnlyDictionary<string, Size> OriginalSize
            = Data.File(@"dsr/size.json").LoadJsonFile<Dictionary<string, Size>>();
        public static void CreateSize(IProgressToken token)
        {
            token.SubmitStatus("Size");

            var dds = Directory.GetFiles(ExtractDir, "*.dds", SearchOption.AllDirectories);

            var size = new Dictionary<string, Size>();
            token.ForAllParallel(dds, file =>
            {
                try
                {
                    var (header, _) = file.ReadDdsHeader();
                    lock (size) { size[file] = new Size((int)header.Width, (int)header.Height); }
                }
                catch (System.Exception e)
                {
                    token.LogException(e);
                }
            });

            size.SaveAsJson(Data.File(@"dsr/size.json", Data.Source.Local));
        }

        public static IReadOnlyDictionary<string, DDSFormat> OriginalFormat
            = Data.File(@"dsr/format.json").LoadJsonFile<Dictionary<string, DDSFormat>>();
        public static void CreateFormat(IProgressToken token)
        {
            token.SubmitStatus("Format");

            var dds = Directory.GetFiles(ExtractDir, "*.dds", SearchOption.AllDirectories);

            var format = new Dictionary<string, DDSFormat>();
            token.ForAllParallel(dds, file =>
            {
                try
                {
                    var f = file.ReadDdsHeader().GetFormat();
                    lock (format) { format[file] = f; }
                }
                catch (System.Exception e)
                {
                    token.LogException(e);
                }
            });

            format.SaveAsJson(Data.File(@"dsr/format.json", Data.Source.Local));
        }

        public static IReadOnlyDictionary<string, TexKind> TexKinds
            = Data.File(@"dsr/tex-kind.json").LoadJsonFile<Dictionary<string, TexKind>>();
        public static void CreateTexKind(IProgressToken token)
        {
            token.SubmitStatus("Tex kind");

            var dds = Directory.GetFiles(ExtractDir, "*.dds", SearchOption.AllDirectories);

            var kind = new Dictionary<string, TexKind>();
            token.ForAll(dds, file =>
            {
                var k = TexId.GuessTexKind(Path.GetFileNameWithoutExtension(file));
                kind[file] = k == TexKind.Unknown ? TexKind.Albedo : k;
            });

            kind.SaveAsJson(Data.File(@"dsr/tex-kind.json", Data.Source.Local));
        }

        public static ExternalReuse Similar = new ExternalReuse()
        {
            CertainFile = @"dsr/similar.json",
            UncertainFile = @"dsr/similar-uncertain.json",
            RejectedFile = @"dsr/similar-rejected.json",

            ExternalDir = ExtractDir,
            ExternalSize = OriginalSize,

            Ds3Filter = id =>
            {
                if (id.GetRepresentative() != id) return false;
                if (id.IsSolidColor()) return false;
                if (id.GetTexKind() == TexKind.Normal) return false;
                if (id.GetTexKind() == TexKind.Reflective) return false;
                if (id.GetTexKind() == TexKind.Emissive) return false;
                if (id.GetTexKind() == TexKind.Mask) return false;
                return true;
            },
            ExternalFilter = file => TexKinds[file] != TexKind.Normal,

            RequireSize = ExternalReuse.SizeReq.GtOrEq,
            SameKind = false,

            CopySpread = image => 6,
            MaxDiff = new Rgba32(2, 2, 2, 100),
        };

        public static ExternalReuse AlphaReuse = new ExternalReuse()
        {
            CertainFile = @"dsr/copy-alpha.json",
            UncertainFile = @"dsr/copy-alpha-uncertain.json",
            RejectedFile = @"dsr/copy-alpha-rejected.json",

            ExternalDir = ExtractDir,
            ExternalSize = OriginalSize,

            Ds3Filter = id =>
            {
                if (id != id.GetAlphaRepresentative()) return false;
                if (id.IsSolidColor()) return false;
                var t = id.GetTransparency();
                if (t != TransparencyKind.Binary && t != TransparencyKind.Full) return false;

                return true;
            },
            ExternalFilter = file =>
            {
                var t = Alpha.GetOrDefault(file, TransparencyKind.None);
                if (t != TransparencyKind.Binary && t != TransparencyKind.Full) return false;

                return true;
            },

            CopyHasherFactory = Hasher.Alpha(),
            CopySpread = image => 6,
            MaxDiff = new Rgba32(255, 255, 255, 2),
            ModifyImage = image =>
            {
                foreach (ref var p in image.Data.AsSpan())
                {
                    p.R = p.A;
                    p.G = p.A;
                    p.B = p.A;
                    p.A = 255;
                }
            },
        };

        public static ExternalReuse NormalReuse = new ExternalReuse()
        {
            CertainFile = (@"dsr/copy-normal.json"),
            UncertainFile = (@"dsr/copy-normal-uncertain.json"),
            RejectedFile = (@"dsr/copy-normal-rejected.json"),

            ExternalDir = ExtractDir,
            ExternalSize = OriginalSize,

            Ds3Filter = id =>
            {
                if (id.GetNormalRepresentative() != id) return false;
                if (id.IsSolidColor()) return false;
                if (id.GetTexKind() != TexKind.Normal) return false;
                var diff = DS3.OriginalColorDiff[id];
                if (diff.R < 15 && diff.G < 12) return false;
                return true;
            },
            ExternalFilter = file => TexKinds[file] == TexKind.Normal,

            CopyHasherFactory = Hasher.NormalDirection(mid: false),
            CopySpread = image => 20,
            MaxDiff = new Rgba32(2, 2, 255, 255),
            ModifyImage = image =>
            {
                image.Multiply(new Rgba32(255, 255, 0, 0));
                image.SetAlpha(255);
            },
        };

        // public static ExternalReuse GlossReuse = new ExternalReuse()
        // {
        //     CertainFile = (@"er/copy-gloss.json"),
        //     UncertainFile = (@"er/copy-gloss-uncertain.json"),
        //     RejectedFile = (@"er/copy-gloss-rejected.json"),

        //     ExternalDir = ExtractDir,
        //     ExternalSize = OriginalSize,

        //     Ds3Filter = id =>
        //     {
        //         if (id.GetGlossRepresentative() != id) return false;
        //         if (id.IsSolidColor()) return false;
        //         if (id.GetTexKind() != TexKind.Normal) return false;
        //         // ignore all gloss maps that are just solid colors
        //         if (DS3.OriginalColorDiff[id].B <= 12) return false;
        //         return true;
        //     },
        //     ExternalFilter = file => TexKinds[file] == TexKind.Normal,

        //     CopyHasherFactory = r => new BlueChannelImageHasher(r),
        //     CopySpread = image => image.Count <= 64 * 64 ? 8 : image.Count <= 128 * 128 ? 5 : 3,
        //     MaxDiff = new Rgba32(255, 255, 8, 255),
        //     ModifyImage = image =>
        //     {
        //         foreach (ref var p in image.Data.AsSpan())
        //         {
        //             p.R = p.B;
        //             p.G = p.B;
        //             p.A = 255;
        //         }
        //     },
        // };

        // public static IReadOnlyDictionary<TexId, string> GeneralRepresentative
        //     = Data.File(@"er/representative-general.json").LoadJsonFile<Dictionary<TexId, string>>();
        // public static IReadOnlyDictionary<TexId, string> AlpaRepresentative
        //     = Data.File(@"er/representative-alpha.json").LoadJsonFile<Dictionary<TexId, string>>();
        // public static IReadOnlyDictionary<TexId, string> NormalRepresentative
        //     = Data.File(@"er/representative-normal.json").LoadJsonFile<Dictionary<TexId, string>>();
        // public static IReadOnlyDictionary<TexId, string> GlossRepresentative
        //     = Data.File(@"er/representative-gloss.json").LoadJsonFile<Dictionary<TexId, string>>();

        // public static void CreateGeneralRepresentative(IProgressToken token)
        // {
        //     var certain = GeneralReuse.CertainFile.Read.LoadJsonFile<Dictionary<TexId, HashSet<string>>>();

        //     var rep = new Dictionary<TexId, string>();
        //     token.ForAll(certain, kv =>
        //     {
        //         var (id, copies) = kv;
        //         var best = GetHighestQualityCopy(copies);
        //         if (CopyIsHigherQuality(id, best))
        //         {
        //             rep[id] = best;
        //         }
        //     });

        //     rep.SaveAsJson(Data.File(@"er/representative-general.json"));
        // }
        // public static void CreateAlphaRepresentative(IProgressToken token)
        // {
        //     var certain = AlphaReuse.CertainFile.Read.LoadJsonFile<Dictionary<TexId, HashSet<string>>>();

        //     var rep = new Dictionary<TexId, string>();
        //     token.ForAll(certain, kv =>
        //     {
        //         var (id, copies) = kv;

        //         if (id.GetTransparency() == TransparencyKind.Full)
        //         {
        //             copies.RemoveWhere(f => Alpha[f] != TransparencyKind.Full);
        //         }
        //         if (copies.Count == 0) return;

        //         var best = GetHighestQualityCopy(copies);
        //         if (CopyIsHigherQuality(id, best))
        //         {
        //             rep[id] = best;
        //         }
        //     });

        //     rep.SaveAsJson(Data.File(@"er/representative-alpha.json"));
        // }
        // public static void CreateNormalRepresentative(IProgressToken token)
        // {
        //     var certain = NormalReuse.CertainFile.Read.LoadJsonFile<Dictionary<TexId, HashSet<string>>>();

        //     var rep = new Dictionary<TexId, string>();
        //     token.ForAll(certain, kv =>
        //     {
        //         var (id, copies) = kv;
        //         var best = GetHighestQualityCopy(copies);
        //         if (CopyIsHigherQuality(id, best))
        //         {
        //             rep[id] = best;
        //         }
        //     });

        //     rep.SaveAsJson(Data.File(@"er/representative-normal.json"));
        // }
        // public static void CreateGlossRepresentative(IProgressToken token)
        // {
        //     var certain = GlossReuse.CertainFile.Read.LoadJsonFile<Dictionary<TexId, HashSet<string>>>();

        //     var rep = new Dictionary<TexId, string>();
        //     token.ForAll(certain, kv =>
        //     {
        //         var (id, copies) = kv;
        //         var best = GetHighestQualityCopy(copies);
        //         if (CopyIsHigherQuality(id, best))
        //         {
        //             rep[id] = best;
        //         }
        //     });

        //     rep.SaveAsJson(Data.File(@"er/representative-gloss.json"));
        // }

        // private static string GetHighestQualityCopy(IEnumerable<string> copies)
        // {
        //     var l = copies.ToList();
        //     l.Sort((a, b) =>
        //     {
        //         var q = CompareQuality(
        //             (OriginalSize[a], OriginalFormat[a]),
        //             (OriginalSize[b], OriginalFormat[b])
        //         );
        //         if (q != 0) return q;
        //         return -a.CompareTo(b);
        //     });
        //     return l.Last();
        // }
        // private static int CompareQuality((Size Size, DDSFormat Format) a, (Size Size, DDSFormat Format) b)
        // {
        //     var s = a.Size.Width.CompareTo(b.Size.Width);
        //     if (s != 0) return s;

        //     var af = a.Format.QualityScore;
        //     var bf = b.Format.QualityScore;
        //     if (af != null && bf != null && af != bf)
        //     {
        //         return af.Value.CompareTo(bf.Value);
        //     }

        //     return 0;
        // }
        // private static bool CopyIsHigherQuality(TexId id, string copy)
        // {
        //     var q = CompareQuality(
        //         (DS3.OriginalSize[id], DS3.OriginalFormat[id]),
        //         (OriginalSize[copy], OriginalFormat[copy])
        //     );
        //     return q < 0;
        // }

        // public static void CreateUpscaleDirectory(IProgressToken token)
        // {
        //     token.SplitEqually(
        //         CreateUpscaleDirectoryGeneral,
        //         CreateUpscaleDirectoryAlpha,
        //         CreateUpscaleDirectoryNormal,
        //         CreateUpscaleDirectoryGloss
        //     );
        // }
        // private static void CreateUpscaleDirectoryGeneral(IProgressToken token)
        // {
        //     token.SubmitStatus("General");
        //     token.ForAllParallel(GeneralRepresentative, kv =>
        //     {
        //         var (id, reFile) = kv;

        //         var kind = id.GetTexKind() switch
        //         {
        //             TexKind.Albedo => "a",
        //             TexKind.Reflective => "r",
        //             _ => "unknown"
        //         };

        //         var t = Alpha[reFile];

        //         var target = Path.Join(UpscaleDir, kind, id.Category, $"{id.Name.ToString()}.png");
        //         Directory.CreateDirectory(Path.GetDirectoryName(target));

        //         if (t == TransparencyKind.Binary || t == TransparencyKind.Full)
        //         {
        //             var image = reFile.LoadTextureMap();

        //             var alphaKind = t == TransparencyKind.Binary ? "alpha_binary" : "alpha_full";
        //             var alphaTarget = Path.Join(UpscaleDir, alphaKind, id.Category, $"{id.Name.ToString()}.png");
        //             Directory.CreateDirectory(Path.GetDirectoryName(alphaTarget));
        //             image.GetAlpha().SaveAsPng(alphaTarget);

        //             image.FillSmallHoles3();
        //             image.SetBackground(default);
        //             image.SaveAsPng(target);
        //         }
        //         else
        //         {
        //             reFile.ToPNG(target);
        //         }
        //     });
        // }
        // private static void CreateUpscaleDirectoryAlpha(IProgressToken token)
        // {
        //     token.SubmitStatus("Alpha");
        //     token.ForAllParallel(AlpaRepresentative, kv =>
        //     {
        //         var (id, reFile) = kv;

        //         var kind = id.GetTexKind() switch
        //         {
        //             TexKind.Normal => "n_height",
        //             _ => $"alpha_{(Alpha[reFile] == TransparencyKind.Full ? "full" : "binary")}",
        //         };
        //         var target = Path.Join(UpscaleDir, kind, id.Category, $"{id.Name.ToString()}.png");

        //         Directory.CreateDirectory(Path.GetDirectoryName(target));
        //         if (File.Exists(target)) File.Delete(target);
        //         reFile.LoadTextureMap().GetAlpha().SaveAsPng(target);
        //     });
        // }
        // private static void CreateUpscaleDirectoryNormal(IProgressToken token)
        // {
        //     token.SubmitStatus("Normal");
        //     token.ForAllParallel(NormalRepresentative, kv =>
        //     {
        //         var (id, reFile) = kv;

        //         var target = Path.Join(UpscaleDir, "n_normal", id.Category, $"{id.Name.ToString()}.png");

        //         Directory.CreateDirectory(Path.GetDirectoryName(target));
        //         DS3NormalMap.Load(reFile).Normals.SaveAsPng(target);
        //     });
        // }
        // private static void CreateUpscaleDirectoryGloss(IProgressToken token)
        // {
        //     token.SubmitStatus("Gloss");
        //     token.ForAllParallel(GlossRepresentative, kv =>
        //     {
        //         var (id, reFile) = kv;

        //         var target = Path.Join(UpscaleDir, "n_gloss", id.Category, $"{id.Name.ToString()}.png");

        //         Directory.CreateDirectory(Path.GetDirectoryName(target));
        //         DS3NormalMap.Load(reFile).Gloss.SaveAsPng(target);
        //     });
        // }
    }
}
