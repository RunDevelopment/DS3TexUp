using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DS3TexUpUI
{
    public static class ImageExtensions
    {
        public static ArrayTextureMap<Rgba32> LoadTextureMap(this string file)
        {
            if (file.EndsWith(".dds"))
            {
                using var image = DDSImage.Load(file);
                return image.ToTextureMap();
            }
            else
            {
                using var image = Image.Load(file);
                return image.ToTextureMap();
            }
        }

        public static ArrayTextureMap<Rgba32> ToTextureMap(this DDSImage image)
        {
            static Rgba32[] FromBGR(Span<byte> bytes)
            {
                var data = new Rgba32[bytes.Length / 3];
                for (var i = 0; i < data.Length; i++)
                    data[i] = new Rgba32(bytes[i * 3 + 2], bytes[i * 3 + 1], bytes[i * 3 + 0], 255);
                return data;
            }
            static Rgba32[] FromBGRA(Span<byte> bytes)
            {
                var data = new Rgba32[bytes.Length / 4];
                for (var i = 0; i < data.Length; i++)
                    data[i] = new Rgba32(bytes[i * 4 + 2], bytes[i * 4 + 1], bytes[i * 4 + 0], bytes[i * 4 + 3]);
                return data;
            }

            var data = image.Format switch
            {
                Pfim.ImageFormat.Rgb24 => FromBGR(image.Data),
                Pfim.ImageFormat.Rgba32 => FromBGRA(image.Data),
                _ => throw new Exception("Invalid format: " + image.Format)
            };

            return new ArrayTextureMap<Rgba32>(data, image.Width, image.Height);
        }
        public static ArrayTextureMap<Rgba32> ToTextureMap(this Image image)
        {
            static Rgba32[] MapRows<P>(Image<P> image, Func<P, Rgba32> map) where P : unmanaged, IPixel<P>
            {
                var data = new Rgba32[image.Width * image.Height];
                for (var y = 0; y < image.Height; y++)
                {
                    var stride = image.Width * y;
                    var row = image.GetPixelRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                        data[stride + x] = map(row[x]);
                }
                return data;
            }

            var data = image switch
            {
                Image<Bgr24> i => MapRows(i, p => new Rgba32(p.R, p.G, p.B, 255)),
                Image<Bgra32> i => MapRows(i, p => new Rgba32(p.R, p.G, p.B, p.A)),
                Image<Rgba32> i => MapRows(i, p => p),
                Image<Rgb24> i => MapRows(i, p => new Rgba32(p.R, p.G, p.B, 255)),
                _ => throw new Exception("Invalid format: " + image.PixelType.ToString())
            };
            return new ArrayTextureMap<Rgba32>(data, image.Width, image.Height);
        }

        public static ArrayTextureMap<P> DownSample<P, A>(this ArrayTextureMap<P> map, AverageAccumulatorFactory<P, A> factory, int scale)
              where P : struct
              where A : IAverageAccumulator<P>, new()
        {
            if (scale < 1)
                throw new ArgumentOutOfRangeException(nameof(scale));
            if (map.Width % scale != 0 || map.Height % scale != 0)
                throw new ArgumentException("Map dimensions have to evenly divide factor");

            var w = map.Width / scale;
            var h = map.Height / scale;
            var result = new P[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var a = factory.Create();

                    for (int yOffset = 0; yOffset < scale; yOffset++)
                    {
                        var stride = (y * scale + yOffset) * map.Width;

                        for (int xOffset = 0; xOffset < scale; xOffset++)
                        {
                            a.Add(map[stride + x * scale + xOffset]);
                        }
                    }

                    result[y * w + x] = a.Result;
                }
            }

            return new ArrayTextureMap<P>(result, w, h);
        }

        public static ArrayTextureMap<Rgb24> IgnoreAlpha(this ArrayTextureMap<Rgba32> map)
        {
            var color = new Rgb24[map.Count];

            for (int i = 0; i < color.Length; i++)
            {
                var p = map[i];
                color[i] = new Rgb24(p.R, p.G, p.B);
            }

            return color.AsTextureMap(map.Width);
        }
        public static ArrayTextureMap<byte> GreyScaleIgnoreAlpha(this ArrayTextureMap<Rgba32> map)
        {
            var grey = new byte[map.Count];

            for (int i = 0; i < grey.Length; i++)
            {
                var p = map[i];
                grey[i] = (byte)((p.R + p.B + p.G) / 3);
            }

            return grey.AsTextureMap(map.Width);
        }

        public static (ArrayTextureMap<Rgb24> color, ArrayTextureMap<byte> alpha) SplitAlphaBlack(this ArrayTextureMap<Rgba32> map)
        {
            var color = new Rgb24[map.Count];
            var alpha = new byte[map.Count];

            for (int i = 0; i < color.Length; i++)
            {
                var p = map[i];
                color[i] = new Rgb24((byte)(p.R * p.A / 255), (byte)(p.G * p.A / 255), (byte)(p.B * p.A / 255));
                alpha[i] = p.A;
            }

            return (color.AsTextureMap(map.Width), alpha.AsTextureMap(map.Width));
        }
        public static ArrayTextureMap<Rgba32> CombineAlphaBlack(this ArrayTextureMap<Rgba32> color, ArrayTextureMap<Rgba32> alpha)
        {
            if (color.Width != alpha.Width || color.Height != alpha.Height)
                throw new ArgumentException();

            var w = color.Width;
            var h = color.Height;

            var grey = alpha.GreyScaleIgnoreAlpha();
            // var blur = grey.Blur(1);
            // grey.Min(blur);

            var result = new Rgba32[color.Count];

            static byte NoisePass(byte c) => (byte)Math.Clamp(((c - 5) * 26 / 25), 0, 255);

            for (int i = 0; i < result.Length; i++)
            {
                var rgb = color[i];
                var p = CombineAlphaBlack(rgb.R, rgb.G, rgb.B, NoisePass(grey[i]));
                result[i] = p;
            }

            var foo = RemoveBlackOutline(result.AsTextureMap(w));

            RemoveTransparentNoise(foo.Data);

            return foo;
        }
        private static ArrayTextureMap<Rgba32> RemoveBlackOutline(ArrayTextureMap<Rgba32> data)
        {
            var w = data.Width;
            var h = data.Height;

            const int BrightColorRadius = 4;
            const int MinAlphaRadius = 2;

            static int GetBrightnessScore(Rgba32 color)
            {
                return Math.Max(color.R * color.A, Math.Max(color.G * color.A, color.B * color.A));
            }

            var result = new Rgba32[w * h];
            for (int y = 0; y < h; y++)
            {
                var yMinBrightness = Math.Max(0, y - BrightColorRadius);
                var yMaxBrightness = Math.Min(h - 1, y + BrightColorRadius);
                var yMinAlpha = Math.Max(0, y - MinAlphaRadius);
                var yMaxAlpha = Math.Min(h - 1, y + MinAlphaRadius);

                for (int x = 0; x < w; x++)
                {
                    var index = y * w + x;

                    var xMinBrightness = Math.Max(0, x - BrightColorRadius);
                    var xMaxBrightness = Math.Min(w - 1, x + BrightColorRadius);
                    var xMinAlpha = Math.Max(0, x - MinAlphaRadius);
                    var xMaxAlpha = Math.Min(w - 1, x + MinAlphaRadius);

                    var current = data[index];
                    var currentScore = GetBrightnessScore(current);

                    var max = current;
                    var maxScore = currentScore;
                    static void CompareBrightness(ref Rgba32 max, ref int maxScore, Rgba32 current)
                    {
                        var score = GetBrightnessScore(current);
                        if (score > maxScore)
                        {
                            max = current;
                            maxScore = score;
                        }
                    }

                    for (int i = yMinBrightness; i <= yMaxBrightness; i++)
                        for (int j = xMinBrightness; j <= xMaxBrightness; j++)
                            CompareBrightness(ref max, ref maxScore, data[i * w + j]);

                    var minAlpha = current.A;
                    for (int i = yMinAlpha; i <= yMaxAlpha; i++)
                        for (int j = xMinAlpha; j <= xMaxAlpha; j++)
                            minAlpha = Math.Min(minAlpha, data[i * w + j].A);

                    if (maxScore == 0)
                    {
                        result[index] = current;
                        continue;
                    }

                    var bright = new Rgba32(max.R, max.G, max.B, (byte)(max.A * currentScore / maxScore));

                    var blend = Math.Clamp(Math.Max(current.R, Math.Max(current.G, current.B)) / 64.0f + minAlpha / 64.0f, 0, 1);

                    result[index] = new Rgba32(
                        (byte)(current.R * blend + bright.R * (1 - blend)),
                        (byte)(current.G * blend + bright.G * (1 - blend)),
                        (byte)(current.B * blend + bright.B * (1 - blend)),
                        (byte)(current.A * blend + bright.A * (1 - blend))
                    );
                }
            }

            return result.AsTextureMap(w);
        }
        private static void RemoveTransparentNoise(Span<Rgba32> data)
        {
            foreach (ref var item in data)
            {
                if (item.A <= 5)
                    item = new Rgba32(0, 0, 0, 0);
            }
        }
        private static Rgba32 CombineAlphaBlack(byte r, byte g, byte b, byte a)
        {
            if (a == 0)
                return new Rgba32(0, 0, 0, a);
            else
                return new Rgba32(
                    (byte)Math.Min(255, r * 255 / a),
                    (byte)Math.Min(255, g * 255 / a),
                    (byte)Math.Min(255, b * 255 / a),
                    a
                );
        }

        public static ArrayTextureMap<Rgb24> WithBackground(this ArrayTextureMap<Rgba32> map, Rgb24 background)
        {
            var result = new Rgb24[map.Count];

            for (int i = 0; i < result.Length; i++)
            {
                var p = map[i];
                result[i] = new Rgb24(
                    (byte)(p.R * p.A / 255 + background.R * (255 - p.A) / 255),
                    (byte)(p.G * p.A / 255 + background.G * (255 - p.A) / 255),
                    (byte)(p.B * p.A / 255 + background.B * (255 - p.A) / 255)
                );
            }

            return result.AsTextureMap(map.Width);
        }

        public static ArrayTextureMap<Rgba32> CombineWithBackground(this ArrayTextureMap<Rgba32> map1, ArrayTextureMap<Rgba32> map2, Rgb24 bg1, Rgb24 bg2)
        {
            if (map1.Width != map2.Width || map1.Height != map2.Height)
                throw new ArgumentException();

            var result = new Rgba32[map1.Count];

            for (int i = 0; i < result.Length; i++)
            {
                var c1 = map1[i];
                var c2 = map2[i];

                result[i] = CombineWithBackground(new Rgb24(c1.R, c1.G, c1.B), new Rgb24(c2.R, c2.G, c2.B), bg1, bg2);
            }

            return result.AsTextureMap(map1.Width);
        }
        private static Rgba32 CombineWithBackground(Rgb24 c1, Rgb24 c2, Rgb24 bg1, Rgb24 bg2)
        {
            var (cR, cG, cB) = (c1.R - c2.R, c1.G - c2.G, c1.B - c2.B);
            var (bR, bG, bB) = (bg1.R - bg2.R, bg1.G - bg2.G, bg1.B - bg2.B);

            float aTotal = 0;
            int count = 0;

            static void HandleChannel(ref int count, ref float a, int c, int b)
            {
                if (b != 0)
                {
                    a += Math.Clamp(1 - c / (float)b, 0, 1);
                    count++;
                }
            }

            HandleChannel(ref count, ref aTotal, cR, bR);
            HandleChannel(ref count, ref aTotal, cG, bG);
            HandleChannel(ref count, ref aTotal, cB, bB);

            var a = (byte)(aTotal / count * 255);
            if (a == 0) return new Rgba32(0, 0, 0, 0);

            var r = (byte)Math.Clamp(255 * (c1.R - bg1.R) / a + bg1.R, 0, 255);
            var g = (byte)Math.Clamp(255 * (c1.G - bg1.G) / a + bg1.G, 0, 255);
            var b = (byte)Math.Clamp(255 * (c1.B - bg1.B) / a + bg1.B, 0, 255);

            return new Rgba32(r, g, b, a);
        }

        public static ArrayTextureMap<byte> Blur(this ArrayTextureMap<byte> map, int radius)
        {
            if (radius == 0) throw new ArgumentException();

            var w = map.Width;
            var h = map.Height;

            var result = new byte[w * h];

            for (int y = 0; y < h; y++)
            {
                var yMin = Math.Max(0, y - radius);
                var yMax = Math.Min(h - 1, y + radius);
                for (int x = 0; x < w; x++)
                {
                    var xMin = Math.Max(0, x - radius);
                    var xMax = Math.Min(w - 1, x + radius);

                    var total = 0;
                    for (int i = yMin; i <= yMax; i++)
                        for (int j = xMin; j <= xMax; j++)
                            total += map[i * w + j];

                    var count = (yMax - yMin + 1) * (xMax - xMin + 1);
                    result[y * w + x] = (byte)(total / count);
                }
            }

            return result.AsTextureMap(w);
        }
        public static void Min(this ArrayTextureMap<byte> map, ArrayTextureMap<byte> other)
        {
            var w = map.Width;
            var h = map.Height;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var index = y * w + x;
                    map[index] = Math.Min(map[index], other[index]);
                }
            }
        }

        private static (byte y, byte cb, byte cr) RgbToYCbCr(byte r, byte g, byte b)
        {
            return (
                (byte)Math.Clamp((int)(0f + 0.299f * r + 0.587f * g + 0.114f * b), 0, 255),
                (byte)Math.Clamp((int)(128f - 0.168736f * r + 0.331264f * g + 0.5f * b), 0, 255),
                (byte)Math.Clamp((int)(128f + 0.5f * r + 0.418688f * g + 0.081312f * b), 0, 255)
            );
        }
        private static Rgb24 YCbCrToRgb(byte y, byte cb, byte cr)
        {
            return new Rgb24(
                (byte)Math.Clamp((int)(y + 1.402f * (cr - 128)), 0, 255),
                (byte)Math.Clamp((int)(y - 0.344136f * (cb - 128) - 0.714136f * (cr - 128)), 0, 255),
                (byte)Math.Clamp((int)(y + 1.772f * (cb - 128)), 0, 255)
            );
        }

        private static (byte l, byte x, byte y) RgbToLxy(byte r, byte g, byte b)
        {
            var l = Math.Max(r, Math.Max(g, b));
            return (
                l,
                (byte)((255 + r - g) / (2 * l)),
                (byte)((255 + b - g) / (2 * l))
            );
        }
        public static Rgb24 LxyToRgb(byte l, byte x, byte y)
        {
            var g_ = 255 - Math.Max(x, y);
            var max = Math.Max(x, Math.Max(g_, y));
            return new Rgb24((byte)(x * l / max), (byte)(g_ * l / max), (byte)(y * l / max));
        }
    }
}
