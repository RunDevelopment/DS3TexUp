using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace DS3TexUpUI
{
    public static class PngExtensions
    {
        public static void SaveAsPng(this ITextureMap<byte> map, string file)
        {
            var rgb = new Rgb24[map.Count];
            for (int i = 0; i < rgb.Length; i++)
            {
                var v = map[i];
                rgb[i] = new Rgb24(v, v, v);
            }
            rgb.AsTextureMap(map.Width).SaveAsPng(file);
        }
        public static void SaveAsPng(this ITextureMap<float> map, string file)
        {
            var rgb = new Rgb24[map.Count];
            for (int i = 0; i < rgb.Length; i++)
            {
                var f = Math.Clamp(map[i], 0f, 1f) * 255f;
                var v = (byte)f;
                rgb[i] = new Rgb24(v, v, v);
            }
            rgb.AsTextureMap(map.Width).SaveAsPng(file);
        }
        public static void SaveAsPng(this ITextureMap<Normal> map, string file)
        {
            var rgb = new Rgb24[map.Count];
            for (int i = 0; i < rgb.Length; i++)
            {
                var (r, g, b) = map[i].ToRGB();
                rgb[i] = new Rgb24(r, g, b);
            }
            rgb.AsTextureMap(map.Width).SaveAsPng(file);
        }
        public static void SaveAsPng(this ITextureMap<Rgba32> map, string file)
        {
            var rgb = new Rgba32[map.Count];
            for (int i = 0; i < rgb.Length; i++)
                rgb[i] = map[i];

            rgb.AsTextureMap(map.Width).SaveAsPng(file);
        }
        public static void SaveAsPng(this ITextureMap<Rgb24> map, string file)
        {
            var rgb = new Rgb24[map.Count];
            for (int i = 0; i < rgb.Length; i++)
                rgb[i] = map[i];

            rgb.AsTextureMap(map.Width).SaveAsPng(file);
        }

        public static void SaveAsPng(this ArrayTextureMap<Rgba32> map, string file)
        {
            using var image = Image.LoadPixelData(map.Data, map.Width, map.Height);
            image.SaveAsPngWithDefaultEncoder(file);
        }
        public static void SaveAsPng(this ArrayTextureMap<Rgb24> map, string file)
        {
            using var image = Image.LoadPixelData(map.Data, map.Width, map.Height);
            image.SaveAsPngWithDefaultEncoder(file);
        }

        public static void SaveAsPngWithDefaultEncoder(this Image image, string file)
        {
            var encoder = new PngEncoder();
            encoder.Gamma = 1 / 2.19995f;
            encoder.ChunkFilter = PngChunkFilter.None;
            image.SaveAsPng(file);
        }
    }
}
