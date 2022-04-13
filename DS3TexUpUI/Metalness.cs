using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public static byte Estimate(MaterialPoint p)
        {
            var (a, s, r) = p;
            static byte ToByte(float f) => (byte)Math.Clamp((int)(f * 255), 0, 255);

            HSV rHsv = r;

            return Math.Max(
                ToByte((rHsv.S * rHsv.V).ExtendOut(0.125f, 0.25f)),
                ToByte(rHsv.V.ExtendOut(0.5f, 0.7f))
            );
        }

        public static ArrayTextureMap<byte> Estimate(ref ArrayTextureMap<Rgba32> a, ref ArrayTextureMap<byte> s, ArrayTextureMap<Rgba32> r)
        {
            if (SizeRatio.Of(r) != SizeRatio.Of(a) || SizeRatio.Of(r) != SizeRatio.Of(s))
                throw new Exception("Incompatible size ratios");

            if (a.Width > r.Width) a = a.DownSample(Average.Rgba32GammaAlpha, a.Width / r.Width);
            else if (a.Width < r.Width) a = a.UpSample(r.Width / a.Width);
            if (s.Width > r.Width) s = s.DownSample(Average.Byte, s.Width / r.Width);
            else if (s.Width < r.Width) s = s.UpSample(r.Width / s.Width);

            r.CheckSameSize(a);
            r.CheckSameSize(s);

            var result = new byte[a.Count];
            for (var i = 0; i < result.Length; i++)
                result[i] = Estimate(new MaterialPoint(a[i].Rgb, s[i], r[i].Rgb));
            return result.AsTextureMap(r.Width);
        }

        public static void Test(Workspace w, DS3.AlbedoNormalReflective t, string targetDir)
        {
            var a = w.GetExtractPath(t.A).LoadTextureMap();
            a.SetAlpha(255);

            var s = w.GetExtractPath(t.N).LoadTextureMap().GetBlue();

            var r = w.GetExtractPath(t.R).LoadTextureMap();
            r.SetAlpha(255);

            var m = Metalness.Estimate(ref a, ref s, r);

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
                        else if (a.Width < r.Width) a = a.UpSample(r.Width / a.Width);
                        if (s.Width > r.Width) s = s.DownSample(Average.Byte, s.Width / r.Width);
                        else if (s.Width < r.Width) s = s.UpSample(r.Width / s.Width);

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
