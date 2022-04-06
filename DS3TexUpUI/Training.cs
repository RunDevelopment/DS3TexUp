using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Pfim;
using SixLabors.ImageSharp.PixelFormats;

namespace DS3TexUpUI
{
    internal static class Training
    {
        public static void UnzipColorData(IProgressToken token)
        {
            const string ZipDir = @"D:\PBR materials";
            const string TargetDir = @"C:\Users\micha\Desktop\train\raw-data-wood";

            var zips = Directory.GetFiles(ZipDir, "*.zip", SearchOption.AllDirectories);

            token.ForAllParallel(zips, zip =>
            {
                var name = Path.GetFileName(zip);
                var category = Path.GetFileName(Path.GetDirectoryName(zip));

                using var archive = ZipFile.OpenRead(zip);
                var albedo = archive.Entries.Where(e =>
                {
                    var name = Path.GetFileNameWithoutExtension(e.Name).ToLowerInvariant();
                    return name.EndsWith("color") || name.EndsWith("albedo");
                }).ToList();

                if (albedo.Count != 1)
                {
                    token.SubmitLog($"Cannot extract albedo from {category}/{name}. Found {albedo.Count} albedos.");
                    // return;
                }

                foreach (var a in albedo)
                {
                    var target = Path.Join(TargetDir, category, a.Name);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));

                    var temp = target + ".temp";
                    using (var zipS = a.Open())
                    using (var fs = File.Create(temp))
                    {
                        zipS.CopyTo(fs);
                    }

                    File.Move(temp, target);
                }
            });
        }
        public static void UnzipNormalData(IProgressToken token)
        {
            const string ZipDir = @"C:\Users\micha\Desktop\train\zip";
            const string TempDir = @"C:\Users\micha\Desktop\train\tem";
            const string TargetDir = @"C:\Users\micha\Desktop\train\unzip";

            var zips = Directory.GetFiles(ZipDir, "*.zip", SearchOption.AllDirectories);

            static void UnzipInto(string zip, string targetDir)
            {
                var info = new ProcessStartInfo();
                info.FileName = "7z";
                info.ArgumentList.Add("x");
                info.ArgumentList.Add(zip);
                info.ArgumentList.Add("-o" + targetDir);

                using var p = Process.Start(info);
                p.WaitForExit();
            }

            token.ForAllParallel(zips, zip =>
            {
                var name = Path.GetFileNameWithoutExtension(zip);
                var category = Path.GetFileName(Path.GetDirectoryName(zip));

                var temp = Path.Join(TempDir, name);
                Directory.CreateDirectory(temp);

                UnzipInto(zip, temp);

                var normals = Directory.GetFiles(temp, "*.png", SearchOption.AllDirectories)
                    .Where(f =>
                    {
                        var name = Path.GetFileNameWithoutExtension(f);
                        // if (!name.Contains("normal", StringComparison.OrdinalIgnoreCase)) return false;
                        if (name.Contains("-ogl", StringComparison.OrdinalIgnoreCase)) return false;
                        if (name.Contains("normalGL", StringComparison.OrdinalIgnoreCase)) return false;
                        return true;
                    })
                    .ToList();

                if (normals.Count != 1)
                {
                    token.SubmitLog($"Cannot extract normal from {category}/{name}. Found {normals.Count} normals.");
                    return;
                }

                var n = normals[0];
                var target = Path.Join(TargetDir, category, $"{name}.png");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Move(n, target);

                Directory.Delete(temp, true);
            });
        }

        const int HRSize = 512;
        const int LRSize = HRSize / 4;

        public static void CreateAlbedoHR(IProgressToken token, string inputDir, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            var images = Directory.GetFiles(inputDir, "*.png", SearchOption.AllDirectories);
            Array.Sort(images);

            token.ForAllParallel(images.Select((x, i) => (x, i)), p =>
            {
                var (imageFile, id) = p;

                var image = imageFile.LoadTextureMap();
                image.SetAlpha(255);

                if (image.Width % HRSize != 0 || image.Height % HRSize != 0 || !image.Width.IsPowerOfTwo() || !image.Height.IsPowerOfTwo())
                {
                    token.SubmitLog($"Cannot processes {imageFile} because of its size.");
                    return;
                }

                var r = new Random(id);

                while (image.Width >= HRSize && image.Height >= HRSize)
                {
                    var totalCuts = image.Width / HRSize * image.Height / HRSize;
                    var maxCount = 16;
                    var chance = Math.Min(1.0, maxCount / (double)totalCuts);
                    for (int x = 0; x < image.Width; x += HRSize)
                    {
                        for (int y = 0; y < image.Height; y += HRSize)
                        {
                            if (r.NextDouble() > chance) continue;

                            token.CheckCanceled();
                            var cut = image.GetCut(x, y, HRSize, HRSize);
                            cut.SaveAsPng(Path.Join(outputDir, $"i{id}-{image.Width}-tile-{x}-{y}-{HRSize}.png"));
                        }
                    }

                    image = image.DownSample(Average.Rgba32GammaAlpha, 2);
                }
            });
        }
        public static void CreateAlbedoLR(IProgressToken token, string inputDir, string outputDir, LRCompression compression, double blur = 0)
        {
            var tempDir = Path.Join(outputDir, "temp");
            Directory.CreateDirectory(tempDir);

            var images = Directory.GetFiles(inputDir, "*.png", SearchOption.AllDirectories);
            Random random = new Random();

            try
            {
                token.ForAllParallel(images, imageFile =>
                {
                    var name = Path.GetFileName(imageFile);
                    var target = Path.Join(outputDir, name);

                    var image = imageFile.LoadTextureMap();
                    if (random.NextDouble() < blur) image = image.Blur(1, Average.Rgba32GammaAlpha);

                    var small = random.NextDouble() < 0.1
                        ? image.DownSample(Average.Rgba32, 4)
                        : image.DownSample(Average.Rgba32GammaAlpha, 4);

                    if (compression == LRCompression.Uncompressed)
                    {
                        small.SaveAsPng(target);
                    }
                    else
                    {
                        var tempPng = Path.Join(tempDir, name);
                        var tempDds = Path.ChangeExtension(tempPng, ".dds");
                        small.SaveAsPng(tempPng);

                        DDSExtensions.ToDDSUsingTexConv(
                            tempPng,
                            tempDds,
                            format: compression == LRCompression.BC1 ? DxgiFormat.BC1_UNORM_SRGB : DxgiFormat.BC7_UNORM_SRGB,
                            dithering: random.NextDouble() < 0.2
                        );

                        tempDds.ToPNG(target);

                        File.Delete(tempPng);
                        File.Delete(tempDds);
                    }
                });
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        public static void CreateNormalHR(IProgressToken token, string inputDir, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            var images = Directory.GetFiles(inputDir, "*.png", SearchOption.AllDirectories);
            Array.Sort(images);

            token.ForAllParallel(images.Select((x, i) => (x, i)), p =>
            {
                var (imageFile, id) = p;

                var image = imageFile.LoadTextureMap();
                image.SetAlpha(255);
                image.Multiply(new Rgba32(255, 255, 0, 255));

                if (image.Width % HRSize != 0 || image.Height % HRSize != 0 || !image.Width.IsPowerOfTwo() || !image.Height.IsPowerOfTwo())
                {
                    token.SubmitLog($"Cannot processes {imageFile} because of its size.");
                    return;
                }

                var r = new Random(id);

                while (image.Width >= HRSize && image.Height >= HRSize)
                {
                    var totalCuts = image.Width / HRSize * image.Height / HRSize;
                    var maxCount = 16;
                    var chance = Math.Min(1.0, maxCount / (double)totalCuts);
                    for (int x = 0; x < image.Width; x += HRSize)
                    {
                        for (int y = 0; y < image.Height; y += HRSize)
                        {
                            if (r.NextDouble() > chance) continue;

                            token.CheckCanceled();
                            var cut = image.GetCut(x, y, HRSize, HRSize);
                            cut.SaveAsPng(Path.Join(outputDir, $"i{id}-{image.Width}-tile-{x}-{y}-{HRSize}.png"));
                        }
                    }

                    image = image.DownSample(Average.Rgba32, 2);
                }
            });
        }
        public static void CreateNormalLR(IProgressToken token, string inputDir, string outputDir, LRCompression compression)
        {
            var tempDir = Path.Join(outputDir, "temp");
            Directory.CreateDirectory(tempDir);

            var images = Directory.GetFiles(inputDir, "*.png", SearchOption.AllDirectories);
            Random random = new Random();

            const string SmallAlbedoDir = @"C:\Users\micha\Desktop\small-albedo";
            ArrayTextureMap<byte>[] smallAlbedo = Directory.GetFiles(SmallAlbedoDir).Select(f => f.LoadTextureMap().GetGreen()).ToArray();

            void SetBlue(ref ArrayTextureMap<Rgba32> map)
            {
                var kind = random.Next(3);
                if (kind == 0)
                {
                    // Z component
                    foreach (ref var c in map.Data.AsSpan())
                    {
                        var n = Normal.FromRG(c.R, c.G);
                        var (_, _, b) = n.ToRGB();
                        c.B = b;
                    }
                }
                else if (kind == 1)
                {
                    // constant color
                    map.SetBlue((byte)random.Next(256));
                }
                else
                {
                    // texture
                    map.SetBlue(smallAlbedo[random.Next(smallAlbedo.Length)]);
                }
            }

            try
            {
                token.ForAllParallel(images, imageFile =>
                {
                    var name = Path.GetFileName(imageFile);
                    var target = Path.Join(outputDir, name);

                    var image = imageFile.LoadTextureMap();
                    var small = image.DownSample(Average.Rgba32, 4);

                    if (compression == LRCompression.Uncompressed)
                    {
                        small.SaveAsPng(target);
                    }
                    else
                    {
                        var tempPng = Path.Join(tempDir, name);
                        var tempDds = Path.ChangeExtension(tempPng, ".dds");
                        SetBlue(ref small);
                        small.SaveAsPng(tempPng);

                        DDSExtensions.ToDDSUsingTexConv(
                            tempPng,
                            tempDds,
                            format: compression == LRCompression.BC1 ? DxgiFormat.BC1_UNORM : DxgiFormat.BC7_UNORM,
                            uniformWeighting: random.NextDouble() < 0.5,
                            dithering: random.NextDouble() < 0.2,
                            minimalCompression: random.NextDouble() < 0.2
                        );

                        var dds = tempDds.LoadTextureMap();
                        dds.Multiply(new Rgba32(255, 255, 0, 255));
                        dds.SaveAsPng(target);

                        File.Delete(tempPng);
                        File.Delete(tempDds);
                    }
                });
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
        public enum LRCompression
        {
            Uncompressed,
            BC1,
            BC7,
        }
        public static void PickValidation(IProgressToken token, string[] dirs, double ratio = 0.1)
        {
            var r = new Random();
            var l = dirs.SelectMany(Directory.GetFiles).GroupBy(Path.GetFileName).Where(g => g.Count() == dirs.Length).ToList();
            l.Sort((a, b) => r.Next(-10, 11));
            l.Sort((a, b) => r.Next(-10, 11));
            l.Sort((a, b) => r.Next(-10, 11));
            l.Sort((a, b) => r.Next(-10, 11));

            token.ForAllParallel(l.Take((int)(l.Count * ratio)), g =>
            {
                foreach (var f in g)
                {
                    var d = Path.GetDirectoryName(f) + "_validation";
                    Directory.CreateDirectory(d);
                    var t = Path.Join(d, Path.GetFileName(f));
                    File.Move(f, t);
                }
            });
        }
    }
}
