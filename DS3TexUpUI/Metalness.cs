using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Pfim;
using SixLabors.ImageSharp.PixelFormats;

namespace DS3TexUpUI
{
    internal static class Metalness
    {
        public static byte Estimate(Rgb24 a, byte s, Rgb24 r)
        {
            static byte ToByte(float f) => (byte)Math.Clamp((int)(f * 255), 0, 255);

            var aHsv = HSV.FromRgb(a);
            var rHsv = HSV.FromRgb(r);

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
                result[i] = Estimate(a[i].Rgb, s[i], r[i].Rgb);
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

    }
}
