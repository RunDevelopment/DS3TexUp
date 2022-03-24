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
        public const string DataDir = @"C:\Users\micha\Git\DS3TexUp\er-data";

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
            = Path.Join(DataDir, "alpha.json").LoadJsonFile<Dictionary<string, TransparencyKind>>();
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

            alpha.SaveAsJson(Path.Join(DataDir, "alpha.json"));
        }

        public static IReadOnlyDictionary<string, Size> OriginalSize
            = Path.Join(DataDir, "size.json").LoadJsonFile<Dictionary<string, Size>>();
        public static void CreateSize(IProgressToken token)
        {
            token.SubmitStatus("Size");

            var dds = Directory.GetFiles(ExtractDir, "*.dds", SearchOption.AllDirectories);

            var size = new Dictionary<string, Size>();
            token.ForAllParallel(dds, file =>
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    var header = new DdsHeader(stream);
                    lock (size) { size[file] = new Size((int)header.Width, (int)header.Height); }
                }
                catch (System.Exception e)
                {
                    token.LogException(e);
                }
            });

            size.SaveAsJson(Path.Join(DataDir, "size.json"));
        }

        public static ExternalReuse GeneralReuse = new ExternalReuse()
        {
            CertainFile = Path.Join(DataDir, "copy-general.json"),
            UncertainFile = Path.Join(DataDir, "copy-general-uncertain.json"),
            RejectedFile = Path.Join(DataDir, "copy-general-rejected.json"),

            ExternalDir = ExtractDir,
            ExternalSize = OriginalSize,

            Ds3Filter = id =>
            {
                if (id.GetRepresentative() != id) return false;
                if (id.IsSolidColor()) return false;
                if (id.GetTexKind() == TexKind.Normal) return false;
                return true;
            },
            ExternalFilter = file => TexId.GuessTexKind(Path.GetFileNameWithoutExtension(file)) != TexKind.Normal,

            RequireGreater = true,
            SameKind = true,

            CopySpread = image => image.Count <= 64 * 64 ? 10 : image.Count <= 128 * 128 ? 8 : 6,
            MaxDiff = new Rgba32(2, 2, 2, 2),
        };

        public static ExternalReuse AlphaReuse = new ExternalReuse()
        {
            CertainFile = Path.Join(DataDir, "copy-alpha.json"),
            UncertainFile = Path.Join(DataDir, "copy-alpha-uncertain.json"),
            RejectedFile = Path.Join(DataDir, "copy-alpha-rejected.json"),

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
            CertainFile = Path.Join(DataDir, "copy-normal.json"),
            UncertainFile = Path.Join(DataDir, "copy-normal-uncertain.json"),
            RejectedFile = Path.Join(DataDir, "copy-normal-rejected.json"),

            ExternalDir = ExtractDir,
            ExternalSize = OriginalSize,

            Ds3Filter = id =>
            {
                if (id.GetNormalRepresentative() != id) return false;
                if (id.IsSolidColor()) return false;
                if (id.GetTexKind() != TexKind.Normal) return false;
                return true;
            },
            ExternalFilter = file => TexId.GuessTexKind(Path.GetFileNameWithoutExtension(file)) == TexKind.Normal,

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
            CertainFile = Path.Join(DataDir, "copy-gloss.json"),
            UncertainFile = Path.Join(DataDir, "copy-gloss-uncertain.json"),
            RejectedFile = Path.Join(DataDir, "copy-gloss-rejected.json"),

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
            ExternalFilter = file => TexId.GuessTexKind(Path.GetFileNameWithoutExtension(file)) == TexKind.Normal,

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

    }
}
