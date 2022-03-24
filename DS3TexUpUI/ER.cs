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
    internal static class ER
    {
        public const string ExtractDir = @"C:\DS3TexUp\extract-er";
        public const string UpscaleDir = @"C:\DS3TexUp\upscale-er";

        public static void ExtractER(IProgressToken token)
        {
            const string GameDir = @"C:\Program Files (x86)\Steam\steamapps\common\ELDEN RING\Game";

            Directory.CreateDirectory(ExtractDir);

            void ExtractAsset(SubProgressToken token)
            {
                token.SubmitStatus("asset");

                var assetDir = Path.Join(GameDir, "asset", "aet");

                var tpf = Directory
                    .GetFiles(assetDir, "*.tpf.dcx", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith("_l.tpf.dcx"))
                    .ToArray();

                token.SubmitStatus("Unpacking asset");
                Yabber.RunParallel(token.Reserve(0.5), tpf);

                token.SubmitStatus("Copying asset");
                var dds = Directory.GetFiles(assetDir, "*.dds", SearchOption.AllDirectories);
                var targetDir = Path.Join(ExtractDir, "asset");
                Directory.CreateDirectory(targetDir);

                token.ForAllParallel(dds, f =>
                {
                    var target = Path.Join(targetDir, Path.GetFileName(f));
                    File.Copy(f, target);
                });
            }
            void ExtractChr(SubProgressToken token)
            {
                token.SubmitStatus("chr");

                var chrDir = Path.Join(GameDir, "chr");

                var texbnd = Directory
                    .GetFiles(chrDir, "*.texbnd.dcx", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith("_l.texbnd.dcx"))
                    .ToArray();

                token.SubmitStatus("Unpacking chr texbnd");
                Yabber.RunParallel(token.Reserve(0.5), texbnd);

                token.SubmitStatus("Unpacking chr tpf");
                var tpf = Directory.GetFiles(chrDir, "*.tpf", SearchOption.AllDirectories);
                Yabber.RunParallel(token.Reserve(0.5), tpf);

                token.SubmitStatus("Copying chr");
                var dds = Directory.GetFiles(chrDir, "*.dds", SearchOption.AllDirectories);
                var targetDir = Path.Join(ExtractDir, "chr");
                Directory.CreateDirectory(targetDir);

                token.ForAllParallel(dds, f =>
                {
                    // c0000
                    var cId = Path.GetFileName(Path.GetDirectoryName(f))!.Substring(0, 5);
                    var target = Path.Join(targetDir, $"{cId}-{Path.GetFileName(f)}");
                    File.Copy(f, target);
                });
            }
            void ExtractParts(SubProgressToken token)
            {
                token.SubmitStatus("parts");

                var partsDir = Path.Join(GameDir, "parts");

                var partsbnd = Directory
                    .GetFiles(partsDir, "*.partsbnd.dcx", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith("_l.partsbnd.dcx"))
                    .ToArray();

                token.SubmitStatus("Unpacking parts partsbnd");
                Yabber.RunParallel(token.Reserve(0.5), partsbnd);

                token.SubmitStatus("Unpacking parts tpf");
                var tpf = Directory.GetFiles(partsDir, "*.tpf", SearchOption.AllDirectories);
                Yabber.RunParallel(token.Reserve(0.5), tpf);

                token.SubmitStatus("Copying parts");
                var dds = Directory.GetFiles(partsDir, "*.dds", SearchOption.AllDirectories);
                var targetDir = Path.Join(ExtractDir, "parts");
                Directory.CreateDirectory(targetDir);

                token.ForAllParallel(dds, f =>
                {
                    var id = Path.GetFileName(Path.GetDirectoryName(f))!;
                    id = id.Substring(0, id.Length - "-tpf".Length);
                    var target = Path.Join(targetDir, $"{id}-{Path.GetFileName(f)}");
                    File.Copy(f, target);
                });
            }
            void ExtractSfx(SubProgressToken token)
            {
                token.SubmitStatus("sfx");

                var sfxDir = Path.Join(GameDir, "sfx");

                var ffxbnd = Directory
                    .GetFiles(sfxDir, "*.ffxbnd.dcx", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith("_l.ffxbnd.dcx"))
                    .ToArray();

                token.SubmitStatus("Unpacking sfx ffxbnd");
                Yabber.RunParallel(token.Reserve(0.5), ffxbnd);

                token.SubmitStatus("Unpacking sfx tpf");
                var tpf = Directory.GetFiles(sfxDir, "*.tpf", SearchOption.AllDirectories);
                Yabber.RunParallel(token.Reserve(0.5), tpf);

                token.SubmitStatus("Copying sfx");
                var dds = Directory.GetFiles(sfxDir, "*.dds", SearchOption.AllDirectories);
                var targetDir = Path.Join(ExtractDir, "sfx");
                Directory.CreateDirectory(targetDir);

                token.ForAllParallel(dds, f =>
                {
                    string p = f;
                    while (Path.GetFileName(p) != "GR")
                        p = Path.GetDirectoryName(p)!;
                    var name = Path.GetFileName(Path.GetDirectoryName(p))!;
                    name = name.Substring("sfxbnd_".Length).Substring(0, name.Length - "-ffxbnd-dcx".Length);
                    var target = Path.Join(targetDir, $"{name}-{Path.GetFileName(f)}");
                    File.Copy(f, target);
                });
            }

            token.SplitEqually(
                ExtractAsset,
                ExtractChr,
                ExtractParts,
                ExtractSfx
            );
        }
        public static void RemoveSolidColor(IProgressToken token)
        {
            token.SubmitStatus("Gathering files");

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

        public static IReadOnlyDictionary<string, TransparencyKind> Alpha
            = Data.File(@"er/alpha.json").LoadJsonFile<Dictionary<string, TransparencyKind>>();
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

            alpha.SaveAsJson(Data.File(@"er/alpha.json"));
        }

        public static IReadOnlyDictionary<string, Size> OriginalSize
            = Data.File(@"er/size.json").LoadJsonFile<Dictionary<string, Size>>();
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

            size.SaveAsJson(Data.File(@"er/size.json"));
        }

        public static IReadOnlyDictionary<string, DDSFormat> OriginalFormat
            = Data.File(@"er/format.json").LoadJsonFile<Dictionary<string, DDSFormat>>();
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

            format.SaveAsJson(Data.File(@"er/format.json"));
        }

        public static IReadOnlyDictionary<string, TexKind> TexKinds
            = Data.File(@"er/tex-kind.json").LoadJsonFile<Dictionary<string, TexKind>>();
        public static void CreateTexKind(IProgressToken token)
        {
            token.SubmitStatus("Tex kind");

            var dds = Directory.GetFiles(ExtractDir, "*.dds", SearchOption.AllDirectories);

            var kind = new Dictionary<string, TexKind>();
            token.ForAll(dds, file =>
            {
                kind[file] = TexId.GuessTexKind(Path.GetFileNameWithoutExtension(file));
            });

            kind.SaveAsJson(Data.File(@"er/tex-kind.json"));
        }

        public static ExternalReuse GeneralReuse = new ExternalReuse()
        {
            CertainFile = Data.File(@"er/copy-general.json"),
            UncertainFile = Data.File(@"er/copy-general-uncertain.json"),
            RejectedFile = Data.File(@"er/copy-general-rejected.json"),

            ExternalDir = ExtractDir,
            ExternalSize = OriginalSize,

            Ds3Filter = id =>
            {
                if (id.GetRepresentative() != id) return false;
                if (id.IsSolidColor()) return false;
                if (id.GetTexKind() == TexKind.Normal) return false;
                return true;
            },
            ExternalFilter = file => TexKinds[file] != TexKind.Normal,

            RequireGreater = true,
            SameKind = true,

            CopySpread = image => image.Count <= 64 * 64 ? 10 : image.Count <= 128 * 128 ? 8 : 6,
            MaxDiff = new Rgba32(2, 2, 2, 2),
        };

        public static ExternalReuse AlphaReuse = new ExternalReuse()
        {
            CertainFile = Data.File(@"er/copy-alpha.json"),
            UncertainFile = Data.File(@"er/copy-alpha-uncertain.json"),
            RejectedFile = Data.File(@"er/copy-alpha-rejected.json"),

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
            CopySpread = image => image.Count <= 64 * 64 ? 4 : 2,
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
            CertainFile = Data.File(@"er/copy-normal.json"),
            UncertainFile = Data.File(@"er/copy-normal-uncertain.json"),
            RejectedFile = Data.File(@"er/copy-normal-rejected.json"),

            ExternalDir = ExtractDir,
            ExternalSize = OriginalSize,

            Ds3Filter = id =>
            {
                if (id.GetNormalRepresentative() != id) return false;
                if (id.IsSolidColor()) return false;
                if (id.GetTexKind() != TexKind.Normal) return false;
                return true;
            },
            ExternalFilter = file => TexKinds[file] == TexKind.Normal,

            CopyHasherFactory = r => new NormalImageHasher(r),
            CopySpread = image => image.Count <= 64 * 64 ? 6 : image.Count <= 128 * 128 ? 4 : 3,
            MaxDiff = new Rgba32(2, 2, 255, 255),
            ModifyImage = image =>
            {
                image.Multiply(new Rgba32(255, 255, 0, 0));
                image.SetAlpha(255);
            },
        };

        public static ExternalReuse GlossReuse = new ExternalReuse()
        {
            CertainFile = Data.File(@"er/copy-gloss.json"),
            UncertainFile = Data.File(@"er/copy-gloss-uncertain.json"),
            RejectedFile = Data.File(@"er/copy-gloss-rejected.json"),

            ExternalDir = ExtractDir,
            ExternalSize = OriginalSize,

            Ds3Filter = id =>
            {
                if (id.GetGlossRepresentative() != id) return false;
                if (id.IsSolidColor()) return false;
                if (id.GetTexKind() != TexKind.Normal) return false;
                // ignore all gloss maps that are just solid colors
                if (DS3.OriginalColorDiff[id].B <= 12) return false;
                return true;
            },
            ExternalFilter = file => TexKinds[file] == TexKind.Normal,

            CopyHasherFactory = r => new BlueChannelImageHasher(r),
            CopySpread = image => image.Count <= 64 * 64 ? 8 : image.Count <= 128 * 128 ? 5 : 3,
            MaxDiff = new Rgba32(255, 255, 8, 255),
            ModifyImage = image =>
            {
                foreach (ref var p in image.Data.AsSpan())
                {
                    p.R = p.B;
                    p.G = p.B;
                    p.A = 255;
                }
            },
        };

        public static IReadOnlyDictionary<TexId, string> GeneralRepresentative
            = Data.File(@"er/representative-general.json").LoadJsonFile<Dictionary<TexId, string>>();
        public static IReadOnlyDictionary<TexId, string> AlpaRepresentative
            = Data.File(@"er/representative-alpha.json").LoadJsonFile<Dictionary<TexId, string>>();
        public static IReadOnlyDictionary<TexId, string> NormalRepresentative
            = Data.File(@"er/representative-normal.json").LoadJsonFile<Dictionary<TexId, string>>();
        public static IReadOnlyDictionary<TexId, string> GlossRepresentative
            = Data.File(@"er/representative-gloss.json").LoadJsonFile<Dictionary<TexId, string>>();

        public static void CreateGeneralRepresentative(IProgressToken token)
        {
            var certain = GeneralReuse.CertainFile.LoadJsonFile<Dictionary<TexId, HashSet<string>>>();

            var rep = new Dictionary<TexId, string>();
            token.ForAll(certain, kv =>
            {
                var (id, copies) = kv;
                var best = GetHighestQualityCopy(copies);
                if (CopyIsHigherQuality(id, best))
                {
                    rep[id] = best;
                }
            });

            rep.SaveAsJson(Data.File(@"er/representative-general.json"));
        }
        public static void CreateAlphaRepresentative(IProgressToken token)
        {
            var certain = AlphaReuse.CertainFile.LoadJsonFile<Dictionary<TexId, HashSet<string>>>();

            var rep = new Dictionary<TexId, string>();
            token.ForAll(certain, kv =>
            {
                var (id, copies) = kv;

                if (id.GetTransparency() == TransparencyKind.Full)
                {
                    copies.RemoveWhere(f => Alpha[f] != TransparencyKind.Full);
                }
                if (copies.Count == 0) return;

                var best = GetHighestQualityCopy(copies);
                if (CopyIsHigherQuality(id, best))
                {
                    rep[id] = best;
                }
            });

            rep.SaveAsJson(Data.File(@"er/representative-alpha.json"));
        }
        public static void CreateNormalRepresentative(IProgressToken token)
        {
            var certain = NormalReuse.CertainFile.LoadJsonFile<Dictionary<TexId, HashSet<string>>>();

            var rep = new Dictionary<TexId, string>();
            token.ForAll(certain, kv =>
            {
                var (id, copies) = kv;
                var best = GetHighestQualityCopy(copies);
                if (CopyIsHigherQuality(id, best))
                {
                    rep[id] = best;
                }
            });

            rep.SaveAsJson(Data.File(@"er/representative-normal.json"));
        }
        public static void CreateGlossRepresentative(IProgressToken token)
        {
            var certain = GlossReuse.CertainFile.LoadJsonFile<Dictionary<TexId, HashSet<string>>>();

            var rep = new Dictionary<TexId, string>();
            token.ForAll(certain, kv =>
            {
                var (id, copies) = kv;
                var best = GetHighestQualityCopy(copies);
                if (CopyIsHigherQuality(id, best))
                {
                    rep[id] = best;
                }
            });

            rep.SaveAsJson(Data.File(@"er/representative-gloss.json"));
        }

        private static string GetHighestQualityCopy(IEnumerable<string> copies)
        {
            var l = copies.ToList();
            l.Sort((a, b) =>
            {
                var q = CompareQuality(
                    (OriginalSize[a], OriginalFormat[a]),
                    (OriginalSize[b], OriginalFormat[b])
                );
                if (q != 0) return q;
                return -a.CompareTo(b);
            });
            return l.Last();
        }
        private static int CompareQuality((Size Size, DDSFormat Format) a, (Size Size, DDSFormat Format) b)
        {
            var s = a.Size.Width.CompareTo(b.Size.Width);
            if (s != 0) return s;

            var af = a.Format.QualityScore;
            var bf = b.Format.QualityScore;
            if (af != null && bf != null && af != bf)
            {
                return af.Value.CompareTo(bf.Value);
            }

            return 0;
        }
        private static bool CopyIsHigherQuality(TexId id, string copy)
        {
            var q = CompareQuality(
                (DS3.OriginalSize[id], DS3.OriginalFormat[id]),
                (OriginalSize[copy], OriginalFormat[copy])
            );
            return q < 0;
        }

        public static void CreateUpscaleDirectory(IProgressToken token)
        {
            token.SplitEqually(
                CreateUpscaleDirectoryGeneral,
                CreateUpscaleDirectoryAlpha,
                CreateUpscaleDirectoryNormal,
                CreateUpscaleDirectoryGloss
            );
        }
        private static void CreateUpscaleDirectoryGeneral(IProgressToken token)
        {
            token.SubmitStatus("General");
            token.ForAllParallel(GeneralRepresentative, kv =>
            {
                var (id, reFile) = kv;

                var kind = id.GetTexKind() switch
                {
                    TexKind.Albedo => "a",
                    TexKind.Reflective => "r",
                    _ => "unknown"
                };

                var t = Alpha[reFile];

                var target = Path.Join(UpscaleDir, kind, id.Category, $"{id.Name.ToString()}.png");
                Directory.CreateDirectory(Path.GetDirectoryName(target));

                if (t == TransparencyKind.Binary || t == TransparencyKind.Full)
                {
                    var image = reFile.LoadTextureMap();

                    var alphaKind = t == TransparencyKind.Binary ? "alpha_binary" : "alpha_full";
                    var alphaTarget = Path.Join(UpscaleDir, alphaKind, id.Category, $"{id.Name.ToString()}.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(alphaTarget));
                    image.GetAlpha().SaveAsPng(alphaTarget);

                    image.FillSmallHoles3();
                    image.SetBackground(default);
                    image.SaveAsPng(target);
                }
                else
                {
                    reFile.ToPNG(target);
                }
            });
        }
        private static void CreateUpscaleDirectoryAlpha(IProgressToken token)
        {
            token.SubmitStatus("Alpha");
            token.ForAllParallel(AlpaRepresentative, kv =>
            {
                var (id, reFile) = kv;

                var kind = id.GetTexKind() switch
                {
                    TexKind.Normal => "n_height",
                    _ => $"alpha_{(Alpha[reFile] == TransparencyKind.Full ? "full" : "binary")}",
                };
                var target = Path.Join(UpscaleDir, kind, id.Category, $"{id.Name.ToString()}.png");

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target)) File.Delete(target);
                reFile.LoadTextureMap().GetAlpha().SaveAsPng(target);
            });
        }
        private static void CreateUpscaleDirectoryNormal(IProgressToken token)
        {
            token.SubmitStatus("Normal");
            token.ForAllParallel(NormalRepresentative, kv =>
            {
                var (id, reFile) = kv;

                var target = Path.Join(UpscaleDir, "n_normal", id.Category, $"{id.Name.ToString()}.png");

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                DS3NormalMap.Load(reFile).Normals.SaveAsPng(target);
            });
        }
        private static void CreateUpscaleDirectoryGloss(IProgressToken token)
        {
            token.SubmitStatus("Gloss");
            token.ForAllParallel(GlossRepresentative, kv =>
            {
                var (id, reFile) = kv;

                var target = Path.Join(UpscaleDir, "n_gloss", id.Category, $"{id.Name.ToString()}.png");

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                DS3NormalMap.Load(reFile).Gloss.SaveAsPng(target);
            });
        }
    }
}
