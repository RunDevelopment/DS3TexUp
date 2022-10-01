using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Pfim;
using SixLabors.ImageSharp.PixelFormats;

namespace DS3TexUpUI
{
    internal static class Metalness
    {
        public struct MaterialPoint : IEquatable<MaterialPoint>
        {
            public Rgb24 A { get; set; }
            public byte S { get; set; }
            public Rgb24 R { get; set; }

            public MaterialPoint(Rgb24 a, byte s, Rgb24 r) => (A, S, R) = (a, s, r);

            public void Deconstruct(out Rgb24 a, out byte s, out Rgb24 r) => (a, s, r) = (A, S, R);

            public override bool Equals(object obj) => obj is MaterialPoint other ? Equals(other) : false;
            public bool Equals(MaterialPoint other) => A == other.A && S == other.S && R == other.R;
            public override int GetHashCode() => HashCode.Combine(A, S, R);

            public static bool operator ==(MaterialPoint l, MaterialPoint r) => l.Equals(r);
            public static bool operator !=(MaterialPoint l, MaterialPoint r) => !l.Equals(r);
        }

        public struct DataPoint
        {
            public MaterialPoint Material { get; set; }
            public bool Metal { get; set; }

            public DataPoint(MaterialPoint material, bool metal) => (Material, Metal) = (material, metal);
        }
        public class DataCounter
        {
            public int Metal = 0;
            public int NonMetal = 0;
        }

        private static float Max(float a) => a;
        private static float Max(float a, float b) => a < b ? b : a;
        private static float Max(params float[] a) => a.Max();

        public static byte DefaultEstimator(MaterialPoint p)
        {
            var (a, s, r) = p;

            HSV aHsv = a;
            HSV rHsv = r;

            return Max(
                // Most metals have quite a bright reflective value.
                rHsv.V.ExtendOut(0.4f, 0.7f),
                // Typically, only metals have a noticeable hue.
                // Brightness is only considered to prevent artifacts from noise.
                rHsv.S.ExtendOut(0.05f, 0.25f) * rHsv.V.ExtendOut(0f, 0.1f),
                // Some metals (like somewhat rusty iron) have a bit lower brightness in their reflective value,
                // but are very dark in their alebdo.
                rHsv.V.ExtendOut(0.25f, 0.6f) * (1 - aHsv.V.ExtendOut(0.1f, 0.2f))
            ).ToByteClamp();
        }

        public static ArrayTextureMap<byte> Estimate(ref ArrayTextureMap<Rgba32> a, ref ArrayTextureMap<byte> s, ArrayTextureMap<Rgba32> r, Func<MaterialPoint, byte> estimator, int scale = 1)
        {
            if (SizeRatio.Of(r) != SizeRatio.Of(a) || SizeRatio.Of(r) != SizeRatio.Of(s))
                throw new Exception("Incompatible size ratios");

            if (a.Width > r.Width) a = a.DownSample(Average.Rgba32GammaAlpha, a.Width / r.Width);
            else if (a.Width < r.Width) a = a.UpSample(r.Width / a.Width, BiCubic.Rgba);
            if (s.Width > r.Width) s = s.DownSample(Average.Byte, s.Width / r.Width);
            else if (s.Width < r.Width) s = s.UpSample(r.Width / s.Width, BiCubic.Byte);

            r.CheckSameSize(a);
            r.CheckSameSize(s);

            var f = scale * scale;
            var result = new byte[a.Count / f].AsTextureMap(r.Width / scale);
            for (int y = 0; y < result.Height; y++)
            {
                for (int x = 0; x < result.Width; x++)
                {
                    var iR = y * result.Width + x;
                    var iS = scale * (y * r.Width + x);
                    result[iR] = estimator(new MaterialPoint(a[iS].Rgb, s[iS], r[iS].Rgb));
                }
            }

            if (scale != 1)
            {
                result = result.UpSample(scale, BiCubic.Byte);
            }
            return result;
        }

        public static void Test(Workspace w, DS3.AlbedoNormalReflective t, string targetDir, Func<MaterialPoint, byte> estimator, int scale = 1)
        {
            var a = w.GetExtractPath(t.A).LoadTextureMap();
            a.SetAlpha(255);

            var s = w.GetExtractPath(t.N).LoadTextureMap().GetBlue();

            var r = w.GetExtractPath(t.R).LoadTextureMap();
            r.SetAlpha(255);

            var m = Metalness.Estimate(ref a, ref s, r, estimator, scale);

            Directory.CreateDirectory(targetDir);
            a.SaveAsPng(Path.Join(targetDir, "a.png"));
            s.SaveAsPng(Path.Join(targetDir, "s.png"));
            r.SaveAsPng(Path.Join(targetDir, "r.png"));
            m.SaveAsPng(Path.Join(targetDir, "m.png"));
        }

        public static Action<SubProgressToken> CreateMetalDataDir(string outputDir, Workspace w)
        {
            return token =>
            {
                Directory.Delete(outputDir, true);
                Directory.CreateDirectory(outputDir);

                token.ForAllParallel(DS3.ReflectiveANR.Values.Shuffle(123).Take(300), t =>
                {
                    try
                    {
                        var a = w.GetExtractPath(t.A).LoadTextureMap();
                        var s = w.GetExtractPath(t.N).LoadTextureMap().GetBlue();
                        var r = w.GetExtractPath(t.R).LoadTextureMap();

                        if (a.Width > r.Width) a = a.DownSample(Average.Rgba32GammaAlpha, a.Width / r.Width);
                        else if (a.Width < r.Width) a = a.UpSample(r.Width / a.Width, BiCubic.Rgba);
                        if (s.Width > r.Width) s = s.DownSample(Average.Byte, s.Width / r.Width);
                        else if (s.Width < r.Width) s = s.UpSample(r.Width / s.Width, BiCubic.Byte);

                        var m = new byte[r.Count].AsTextureMap(r.Width);
                        m.Data.AsSpan().Fill(128);

                        var targetDir = Path.Join(outputDir, $"{t.R.Category.ToString()}-{t.R.Name.ToString()}");
                        Directory.CreateDirectory(targetDir);

                        a.SaveAsPng(Path.Join(targetDir, "a.png"));
                        s.SaveAsPng(Path.Join(targetDir, "s.png"));
                        r.SaveAsPng(Path.Join(targetDir, "r.png"));
                        m.SaveAsPng(Path.Join(targetDir, "m.png"));
                    }
                    catch (System.Exception e)
                    {
                        token.LogException($"Failed to process {t}", e);
                    }
                });
            };
        }
        public static Action<SubProgressToken> ReadMetalDataDir(string dataDir)
        {
            return token =>
            {
                var files = Directory.GetFiles(dataDir, "m.png", SearchOption.AllDirectories);

                var results = new Dictionary<MaterialPoint, DataCounter>();

                token.ForAllParallel(files, mFile =>
                {
                    try
                    {
                        var aFile = mFile.Substring(0, mFile.Length - 5) + "a.png";
                        var sFile = mFile.Substring(0, mFile.Length - 5) + "s.png";
                        var rFile = mFile.Substring(0, mFile.Length - 5) + "r.png";

                        var a = aFile.LoadTextureMap();
                        var s = sFile.LoadTextureMap().GreyAverage();
                        var r = rFile.LoadTextureMap();
                        var m = mFile.LoadTextureMap().GetGreen();

                        m.CheckSameSize(a);
                        m.CheckSameSize(s);
                        m.CheckSameSize(r);

                        var localResults = new List<DataPoint>();

                        for (int i = 0; i < m.Count; i++)
                        {
                            var mValue = m[i];
                            var metalness = false;
                            if (mValue == 0) metalness = false;
                            else if (mValue == 255) metalness = true;
                            else continue;

                            localResults.Add(new DataPoint(new MaterialPoint(a[i].Rgb, s[i], r[i].Rgb), metalness));
                        }

                        var groups = localResults
                            .GroupBy(x => x.Material)
                            .ToDictionary(g => g.Key, g =>
                            {
                                var total = g.Count();
                                var metal = g.Count(x => x.Metal);
                                return new DataCounter() { Metal = metal, NonMetal = total - metal };
                            });

                        lock (results)
                        {
                            foreach (var (material, counter) in groups)
                            {
                                var c = results.GetOrAdd(material);
                                c.Metal += counter.Metal;
                                c.NonMetal += counter.NonMetal;
                            }
                        }
                    }
                    catch (System.Exception e)
                    {
                        token.LogException($"Failed to process {mFile}", e);
                    }
                });

                var data = results
                    .Select(p => new DataPoint(p.Key, metal: p.Value.Metal > p.Value.NonMetal))
                    .ToList();

                data.SaveAsJson("foo.json");
            };
        }
    }
}
