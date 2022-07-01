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
    internal static class DS2
    {
        public const string GameDir = @"C:\Program Files (x86)\Steam\steamapps\common\Dark Souls II Scholar of the First Sin\Game";
        public const string ExtractDir = @"C:\DS3TexUp\extract-ds2";

        private static void CopyDDS(string ddsFile, string dir, string? prefix = null)
        {
            var i = ddsFile.LoadTextureMap();
            var target = Path.Join(ExtractDir, dir, (prefix == null ? "" : prefix + "-") + Path.GetFileNameWithoutExtension(ddsFile));

            if (target.EndsWith("_n"))
            {
                var baseName = target.Substring(0, target.Length - 2);
                i.Convert(c => new Rgba32(c.A, c.G, 0)).SaveAsPng(target + ".png");
                i.Convert(c => new Rgba32(c.B, c.B, c.B)).SaveAsPng(baseName + "_ao.png");
                i.Convert(c => new Rgba32(c.R, c.R, c.R)).SaveAsPng(baseName + "_rc.png");
            }
            else if (target.EndsWith("_d"))
            {
                var baseName = target.Substring(0, target.Length - 2);
                i.SaveAsPng(baseName + "_a.png");
            }
            else
            {
                i.SaveAsPng(target + ".png");
            }
        }

        public static Action<IProgressToken> ExtractChr()
        {
            return token =>
            {
                var parts = Path.Join(GameDir, @"model\chr");
                var bnd = Directory.GetFiles(parts, "*.texbnd", SearchOption.AllDirectories);
                Yabber.RunParallel(token, bnd);

                var tpf = Directory.GetFiles(parts, "*.tpf", SearchOption.AllDirectories);
                Yabber.RunParallel(token, tpf);

                var dds = Directory.GetFiles(parts, "*.dds", SearchOption.AllDirectories);
                static string? GetChrId(string path)
                {
                    var name = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)));
                    name = name?.Substring(0, name.Length - "-texbnd".Length);
                    return name;
                }
                token.ForAllParallel(dds, f => CopyDDS(f, "chr", GetChrId(f)));
            };
        }
        public static Action<IProgressToken> ExtractMap()
        {
            return token =>
            {
                var parts = Path.Join(GameDir, @"model\map");
                var bnd = Directory
                    .GetFiles(parts, "*.tpfbhd", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith("_low.tpfbdt"))
                    .ToArray();
                Yabber.RunParallel(token, bnd);

                var tpf = Directory.GetFiles(parts, "*.tpf.dcx", SearchOption.AllDirectories);
                Yabber.RunParallel(token, tpf);

                var dds = Directory.GetFiles(parts, "*.dds", SearchOption.AllDirectories);
                static string? GetMapId(string path) => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)));
                token.ForAllParallel(dds, f => CopyDDS(f, "map", GetMapId(f)));
            };
        }
        public static Action<IProgressToken> ExtractObj()
        {
            return token =>
            {
                var parts = Path.Join(GameDir, @"model\obj");
                var bnd = Directory.GetFiles(parts, "*.bnd", SearchOption.AllDirectories);
                Yabber.RunParallel(token, bnd);

                var tpf = Directory.GetFiles(parts, "*.tpf", SearchOption.AllDirectories);
                Yabber.RunParallel(token, tpf);

                var dds = Directory.GetFiles(parts, "*.dds", SearchOption.AllDirectories);
                static string? GetObjId(string path)
                {
                    var name = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)));
                    name = name?.Substring(0, name.Length - "-bnd".Length);
                    return name;
                }
                token.ForAllParallel(dds, f => CopyDDS(f, "obj", GetObjId(f)));
            };
        }
        public static Action<IProgressToken> ExtractParts()
        {
            return token =>
            {
                var parts = Path.Join(GameDir, @"model\parts");
                var bnd = Directory
                    .GetFiles(parts, "*.bnd", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith("_a.bnd") && !f.EndsWith("_l.bnd"))
                    .ToArray();
                Yabber.RunParallel(token, bnd);

                var tpf = Directory.GetFiles(parts, "*.tpf", SearchOption.AllDirectories);
                Yabber.RunParallel(token, tpf);

                var dds = Directory.GetFiles(parts, "*.dds", SearchOption.AllDirectories);
                token.ForAllParallel(dds, f => CopyDDS(f, "parts"));
            };
        }

        public static void RemoveSolidColor(IProgressToken token)
        {
            token.SubmitStatus("Gathering files");

            var dds = Directory.GetFiles(ExtractDir, "*.png", SearchOption.AllDirectories);

            token.ForAllParallel(dds, file =>
            {
                try
                {
                    var diff = file.LoadTextureMap().GetMaxAbsDiff();
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

        public static IReadOnlyDictionary<string, TransparencyKind> Alpha
            = Data.File(@"ds2/alpha.json").LoadJsonFile<Dictionary<string, TransparencyKind>>();
        public static void CreateAlpha(IProgressToken token)
        {
            token.SubmitStatus("Alpha");

            var dds = Directory.GetFiles(ExtractDir, "*.png", SearchOption.AllDirectories);

            var alpha = new Dictionary<string, TransparencyKind>();
            token.ForAllParallel(dds, file =>
            {
                try
                {
                    var t = file.LoadTextureMap().GetTransparency();
                    lock (alpha) { alpha[file] = t; }
                }
                catch (System.Exception e)
                {
                    token.LogException(e);
                }
            });

            alpha.SaveAsJson(Data.File(@"ds2/alpha.json", Data.Source.Local));
        }

        public static IReadOnlyDictionary<string, Size> OriginalSize
            = Data.File(@"ds2/size.json").LoadJsonFile<Dictionary<string, Size>>();
        public static void CreateSize(IProgressToken token)
        {
            token.SubmitStatus("Size");

            var dds = Directory.GetFiles(ExtractDir, "*.png", SearchOption.AllDirectories);

            var size = new Dictionary<string, Size>();
            token.ForAllParallel(dds, file =>
            {
                try
                {
                    var image = file.LoadTextureMap();
                    lock (size) { size[file] = new Size(image.Width, image.Height); }
                }
                catch (System.Exception e)
                {
                    token.LogException(e);
                }
            });

            size.SaveAsJson(Data.File(@"ds2/size.json", Data.Source.Local));
        }

        public static IReadOnlyDictionary<string, TexKind> TexKinds
            = Data.File(@"ds2/tex-kind.json").LoadJsonFile<Dictionary<string, TexKind>>();
        public static void CreateTexKind(IProgressToken token)
        {
            token.SubmitStatus("Tex kind");

            var dds = Directory.GetFiles(ExtractDir, "*.png", SearchOption.AllDirectories);

            var kind = new Dictionary<string, TexKind>();
            token.ForAll(dds, file =>
            {
                var k = TexId.GuessTexKind(Path.GetFileNameWithoutExtension(file));
                kind[file] = k == TexKind.Unknown ? TexKind.Albedo : k;
            });

            kind.SaveAsJson(Data.File(@"ds2/tex-kind.json", Data.Source.Local));
        }

        public static IReadOnlyDictionary<TexId, HashSet<string>> Similar
            = Data.File(@"ds2/similar.json").LoadJsonFile<Dictionary<TexId, HashSet<string>>>();
        public static ExternalReuse SimilarConfig = new ExternalReuse()
        {
            CertainFile = @"ds2/similar.json",
            UncertainFile = @"ds2/similar-uncertain.json",
            RejectedFile = @"ds2/similar-rejected.json",

            ExternalDir = ExtractDir,
            ExternalSize = OriginalSize,

            Ds3Filter = id =>
            {
                if (id.GetRepresentative() != id) return false;
                if (id.IsSolidColor()) return false;
                if (id.GetTexKind() == TexKind.Normal) return false;
                if (id.GetTexKind() == TexKind.Reflective) return false;
                if (id.GetTexKind() == TexKind.Emissive) return false;
                return true;
            },
            ExternalFilter = file => TexKinds[file] != TexKind.Normal,

            RequireSize = ExternalReuse.SizeReq.Gt,
            SameKind = false,

            CopySpread = image => 5,
            MaxDiff = new Rgba32(2, 2, 2, 100),
        };

        public static IReadOnlyDictionary<TexId, HashSet<string>> AlphaSimilar
            = Data.File(@"ds2/copy-alpha.json").LoadJsonFile<Dictionary<TexId, HashSet<string>>>();
        public static ExternalReuse AlphaSimilarConfig = new ExternalReuse()
        {
            CertainFile = @"ds2/copy-alpha.json",
            UncertainFile = @"ds2/copy-alpha-uncertain.json",
            RejectedFile = @"ds2/copy-alpha-rejected.json",

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

            CopyHasherFactory = r => new AlphaImageHasher(r),
            CopySpread = image => 9,
            RequireSize = ExternalReuse.SizeReq.Gt,
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

        public static IReadOnlyDictionary<TexId, HashSet<string>> NormalSimilar
            = Data.File(@"ds2/copy-normal.json").LoadJsonFile<Dictionary<TexId, HashSet<string>>>();
        public static ExternalReuse NormalSimilarConfig = new ExternalReuse()
        {
            CertainFile = (@"ds2/copy-normal.json"),
            UncertainFile = (@"ds2/copy-normal-uncertain.json"),
            RejectedFile = (@"ds2/copy-normal-rejected.json"),

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

            CopyHasherFactory = r => new NormalImageHasher(r),
            CopySpread = image => 4,
            RequireSize = ExternalReuse.SizeReq.Gt,
            MaxDiff = new Rgba32(2, 2, 255, 255),
            ModifyImage = image =>
            {
                image.Multiply(new Rgba32(255, 255, 0, 0));
                image.SetAlpha(255);
            },
        };

        // // public static ExternalReuse GlossReuse = new ExternalReuse()
        // // {
        // //     CertainFile = (@"er/copy-gloss.json"),
        // //     UncertainFile = (@"er/copy-gloss-uncertain.json"),
        // //     RejectedFile = (@"er/copy-gloss-rejected.json"),

        // //     ExternalDir = ExtractDir,
        // //     ExternalSize = OriginalSize,

        // //     Ds3Filter = id =>
        // //     {
        // //         if (id.GetGlossRepresentative() != id) return false;
        // //         if (id.IsSolidColor()) return false;
        // //         if (id.GetTexKind() != TexKind.Normal) return false;
        // //         // ignore all gloss maps that are just solid colors
        // //         if (DS3.OriginalColorDiff[id].B <= 12) return false;
        // //         return true;
        // //     },
        // //     ExternalFilter = file => TexKinds[file] == TexKind.Normal,

        // //     CopyHasherFactory = r => new BlueChannelImageHasher(r),
        // //     CopySpread = image => image.Count <= 64 * 64 ? 8 : image.Count <= 128 * 128 ? 5 : 3,
        // //     MaxDiff = new Rgba32(255, 255, 8, 255),
        // //     ModifyImage = image =>
        // //     {
        // //         foreach (ref var p in image.Data.AsSpan())
        // //         {
        // //             p.R = p.B;
        // //             p.G = p.B;
        // //             p.A = 255;
        // //         }
        // //     },
        // // };

        // // public static IReadOnlyDictionary<TexId, string> GeneralRepresentative
        // //     = Data.File(@"er/representative-general.json").LoadJsonFile<Dictionary<TexId, string>>();
        // // public static IReadOnlyDictionary<TexId, string> AlpaRepresentative
        // //     = Data.File(@"er/representative-alpha.json").LoadJsonFile<Dictionary<TexId, string>>();
        // // public static IReadOnlyDictionary<TexId, string> NormalRepresentative
        // //     = Data.File(@"er/representative-normal.json").LoadJsonFile<Dictionary<TexId, string>>();
        // // public static IReadOnlyDictionary<TexId, string> GlossRepresentative
        // //     = Data.File(@"er/representative-gloss.json").LoadJsonFile<Dictionary<TexId, string>>();

        // // public static void CreateGeneralRepresentative(IProgressToken token)
        // // {
        // //     var certain = GeneralReuse.CertainFile.Read.LoadJsonFile<Dictionary<TexId, HashSet<string>>>();

        // //     var rep = new Dictionary<TexId, string>();
        // //     token.ForAll(certain, kv =>
        // //     {
        // //         var (id, copies) = kv;
        // //         var best = GetHighestQualityCopy(copies);
        // //         if (CopyIsHigherQuality(id, best))
        // //         {
        // //             rep[id] = best;
        // //         }
        // //     });

        // //     rep.SaveAsJson(Data.File(@"er/representative-general.json"));
        // // }
        // // public static void CreateAlphaRepresentative(IProgressToken token)
        // // {
        // //     var certain = AlphaReuse.CertainFile.Read.LoadJsonFile<Dictionary<TexId, HashSet<string>>>();

        // //     var rep = new Dictionary<TexId, string>();
        // //     token.ForAll(certain, kv =>
        // //     {
        // //         var (id, copies) = kv;

        // //         if (id.GetTransparency() == TransparencyKind.Full)
        // //         {
        // //             copies.RemoveWhere(f => Alpha[f] != TransparencyKind.Full);
        // //         }
        // //         if (copies.Count == 0) return;

        // //         var best = GetHighestQualityCopy(copies);
        // //         if (CopyIsHigherQuality(id, best))
        // //         {
        // //             rep[id] = best;
        // //         }
        // //     });

        // //     rep.SaveAsJson(Data.File(@"er/representative-alpha.json"));
        // // }
        // // public static void CreateNormalRepresentative(IProgressToken token)
        // // {
        // //     var certain = NormalReuse.CertainFile.Read.LoadJsonFile<Dictionary<TexId, HashSet<string>>>();

        // //     var rep = new Dictionary<TexId, string>();
        // //     token.ForAll(certain, kv =>
        // //     {
        // //         var (id, copies) = kv;
        // //         var best = GetHighestQualityCopy(copies);
        // //         if (CopyIsHigherQuality(id, best))
        // //         {
        // //             rep[id] = best;
        // //         }
        // //     });

        // //     rep.SaveAsJson(Data.File(@"er/representative-normal.json"));
        // // }
        // // public static void CreateGlossRepresentative(IProgressToken token)
        // // {
        // //     var certain = GlossReuse.CertainFile.Read.LoadJsonFile<Dictionary<TexId, HashSet<string>>>();

        // //     var rep = new Dictionary<TexId, string>();
        // //     token.ForAll(certain, kv =>
        // //     {
        // //         var (id, copies) = kv;
        // //         var best = GetHighestQualityCopy(copies);
        // //         if (CopyIsHigherQuality(id, best))
        // //         {
        // //             rep[id] = best;
        // //         }
        // //     });

        // //     rep.SaveAsJson(Data.File(@"er/representative-gloss.json"));
        // // }

        // // private static string GetHighestQualityCopy(IEnumerable<string> copies)
        // // {
        // //     var l = copies.ToList();
        // //     l.Sort((a, b) =>
        // //     {
        // //         var q = CompareQuality(
        // //             (OriginalSize[a], OriginalFormat[a]),
        // //             (OriginalSize[b], OriginalFormat[b])
        // //         );
        // //         if (q != 0) return q;
        // //         return -a.CompareTo(b);
        // //     });
        // //     return l.Last();
        // // }
        // // private static int CompareQuality((Size Size, DDSFormat Format) a, (Size Size, DDSFormat Format) b)
        // // {
        // //     var s = a.Size.Width.CompareTo(b.Size.Width);
        // //     if (s != 0) return s;

        // //     var af = a.Format.QualityScore;
        // //     var bf = b.Format.QualityScore;
        // //     if (af != null && bf != null && af != bf)
        // //     {
        // //         return af.Value.CompareTo(bf.Value);
        // //     }

        // //     return 0;
        // // }
        // // private static bool CopyIsHigherQuality(TexId id, string copy)
        // // {
        // //     var q = CompareQuality(
        // //         (DS3.OriginalSize[id], DS3.OriginalFormat[id]),
        // //         (OriginalSize[copy], OriginalFormat[copy])
        // //     );
        // //     return q < 0;
        // // }

        // // public static void CreateUpscaleDirectory(IProgressToken token)
        // // {
        // //     token.SplitEqually(
        // //         CreateUpscaleDirectoryGeneral,
        // //         CreateUpscaleDirectoryAlpha,
        // //         CreateUpscaleDirectoryNormal,
        // //         CreateUpscaleDirectoryGloss
        // //     );
        // // }
        // // private static void CreateUpscaleDirectoryGeneral(IProgressToken token)
        // // {
        // //     token.SubmitStatus("General");
        // //     token.ForAllParallel(GeneralRepresentative, kv =>
        // //     {
        // //         var (id, reFile) = kv;

        // //         var kind = id.GetTexKind() switch
        // //         {
        // //             TexKind.Albedo => "a",
        // //             TexKind.Reflective => "r",
        // //             _ => "unknown"
        // //         };

        // //         var t = Alpha[reFile];

        // //         var target = Path.Join(UpscaleDir, kind, id.Category, $"{id.Name.ToString()}.png");
        // //         Directory.CreateDirectory(Path.GetDirectoryName(target));

        // //         if (t == TransparencyKind.Binary || t == TransparencyKind.Full)
        // //         {
        // //             var image = reFile.LoadTextureMap();

        // //             var alphaKind = t == TransparencyKind.Binary ? "alpha_binary" : "alpha_full";
        // //             var alphaTarget = Path.Join(UpscaleDir, alphaKind, id.Category, $"{id.Name.ToString()}.png");
        // //             Directory.CreateDirectory(Path.GetDirectoryName(alphaTarget));
        // //             image.GetAlpha().SaveAsPng(alphaTarget);

        // //             image.FillSmallHoles3();
        // //             image.SetBackground(default);
        // //             image.SaveAsPng(target);
        // //         }
        // //         else
        // //         {
        // //             reFile.ToPNG(target);
        // //         }
        // //     });
        // // }
        // // private static void CreateUpscaleDirectoryAlpha(IProgressToken token)
        // // {
        // //     token.SubmitStatus("Alpha");
        // //     token.ForAllParallel(AlpaRepresentative, kv =>
        // //     {
        // //         var (id, reFile) = kv;

        // //         var kind = id.GetTexKind() switch
        // //         {
        // //             TexKind.Normal => "n_height",
        // //             _ => $"alpha_{(Alpha[reFile] == TransparencyKind.Full ? "full" : "binary")}",
        // //         };
        // //         var target = Path.Join(UpscaleDir, kind, id.Category, $"{id.Name.ToString()}.png");

        // //         Directory.CreateDirectory(Path.GetDirectoryName(target));
        // //         if (File.Exists(target)) File.Delete(target);
        // //         reFile.LoadTextureMap().GetAlpha().SaveAsPng(target);
        // //     });
        // // }
        // // private static void CreateUpscaleDirectoryNormal(IProgressToken token)
        // // {
        // //     token.SubmitStatus("Normal");
        // //     token.ForAllParallel(NormalRepresentative, kv =>
        // //     {
        // //         var (id, reFile) = kv;

        // //         var target = Path.Join(UpscaleDir, "n_normal", id.Category, $"{id.Name.ToString()}.png");

        // //         Directory.CreateDirectory(Path.GetDirectoryName(target));
        // //         DS3NormalMap.Load(reFile).Normals.SaveAsPng(target);
        // //     });
        // // }
        // // private static void CreateUpscaleDirectoryGloss(IProgressToken token)
        // // {
        // //     token.SubmitStatus("Gloss");
        // //     token.ForAllParallel(GlossRepresentative, kv =>
        // //     {
        // //         var (id, reFile) = kv;

        // //         var target = Path.Join(UpscaleDir, "n_gloss", id.Category, $"{id.Name.ToString()}.png");

        // //         Directory.CreateDirectory(Path.GetDirectoryName(target));
        // //         DS3NormalMap.Load(reFile).Gloss.SaveAsPng(target);
        // //     });
        // // }
    }
}
