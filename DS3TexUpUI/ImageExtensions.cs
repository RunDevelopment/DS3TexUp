using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DS3TexUpUI
{
    public static class ImageExtensions
    {
        internal static void CheckSameSize<A, B>(this ITextureMap<A> a, ITextureMap<B> b)
            where A : struct
            where B : struct
        {
            if (a.Width != b.Width || a.Height != b.Height)
                throw new ArgumentException($"Expected the two images to have the same size.");
        }

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
            static Rgba32[] FromG(Span<byte> bytes)
            {
                var data = new Rgba32[bytes.Length];
                for (var i = 0; i < data.Length; i++)
                    data[i] = new Rgba32(bytes[i], bytes[i], bytes[i], 255);
                return data;
            }
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
                Pfim.ImageFormat.Rgb8 => FromG(image.Data),
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

        public static ArrayTextureMap<T> Clone<T>(this ITextureMap<T> map) where T : struct
        {
            var c = map.Count;
            var copy = new T[c];
            for (var i = 0; i < c; i++)
                copy[i] = map[i];
            return copy.AsTextureMap(map.Width);
        }

        public static ArrayTextureMap<P> DownSample<P, A>(this ITextureMap<P> map, AverageAccumulatorFactory<P, A> factory, int scale)
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
                color[i] = map[i].Rgb;
            return color.AsTextureMap(map.Width);
        }

        public static ArrayTextureMap<byte> GreyAverage(this ArrayTextureMap<Rgba32> map)
        {
            var grey = new byte[map.Count];
            for (int i = 0; i < grey.Length; i++)
                grey[i] = map[i].GetGreyAverage();
            return grey.AsTextureMap(map.Width);
        }
        public static ArrayTextureMap<byte> GreyBrightness(this ArrayTextureMap<Rgba32> map)
        {
            var grey = new byte[map.Count];
            for (int i = 0; i < grey.Length; i++)
                grey[i] = map[i].GetGreyBrightness();
            return grey.AsTextureMap(map.Width);
        }
        public static ArrayTextureMap<byte> GreyMinMaxBlend(this ArrayTextureMap<Rgba32> map)
        {
            var grey = new byte[map.Count];
            for (int i = 0; i < grey.Length; i++)
                grey[i] = map[i].GetGreyMinMaxBlend();
            return grey.AsTextureMap(map.Width);
        }

        public static ArrayTextureMap<byte> GetAlpha(this ArrayTextureMap<Rgba32> map)
        {
            var result = new byte[map.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = map.Data[i].A;
            return result.AsTextureMap(map.Width);
        }
        public static ArrayTextureMap<byte> GetRed(this ArrayTextureMap<Rgba32> map)
        {
            var result = new byte[map.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = map.Data[i].R;
            return result.AsTextureMap(map.Width);
        }
        public static ArrayTextureMap<byte> GetGreen(this ArrayTextureMap<Rgba32> map)
        {
            var result = new byte[map.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = map.Data[i].G;
            return result.AsTextureMap(map.Width);
        }
        public static ArrayTextureMap<byte> GetBlue(this ArrayTextureMap<Rgba32> map)
        {
            var result = new byte[map.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = map.Data[i].B;
            return result.AsTextureMap(map.Width);
        }

        public static void SetNoisyAlpha(this ArrayTextureMap<Rgba32> color, ArrayTextureMap<Rgba32> alpha)
        {
            color.CheckSameSize(alpha);

            var w = color.Width;
            var h = color.Height;

            static byte NoisePass(byte c) => (byte)Math.Clamp(((c - 5) * 26 / 25), 0, 255);

            for (int i = 0; i < color.Data.Length; i++)
                color.Data[i].A = NoisePass(alpha.Data[i].GetGreyMinMaxBlend());
        }
        public static void SetNoisyAlpha(this ArrayTextureMap<Rgba32> color, ArrayTextureMap<byte> alpha)
        {
            color.CheckSameSize(alpha);

            static byte NoisePass(byte c) => (byte)Math.Clamp(((c - 5) * 26 / 25), 0, 255);
            for (int i = 0; i < color.Data.Length; i++)
                color.Data[i].A = NoisePass(alpha.Data[i]);
        }

        public static void SetAlpha(this ArrayTextureMap<Rgba32> color, byte alpha)
        {
            foreach (ref var item in color.Data.AsSpan())
                item.A = alpha;
        }
        public static void SetAlpha(this ArrayTextureMap<Rgba32> color, ArrayTextureMap<byte> alpha)
        {
            color.CheckSameSize(alpha);
            for (int i = 0; i < color.Data.Length; i++)
                color.Data[i].A = alpha.Data[i];
        }
        public static void SetRed(this ArrayTextureMap<Rgba32> color, byte red)
        {
            foreach (ref var item in color.Data.AsSpan())
                item.R = red;
        }
        public static void SetRed(this ArrayTextureMap<Rgba32> color, ArrayTextureMap<byte> red)
        {
            color.CheckSameSize(red);
            for (int i = 0; i < color.Data.Length; i++)
                color.Data[i].R = red.Data[i];
        }
        public static void SetGreen(this ArrayTextureMap<Rgba32> color, byte green)
        {
            foreach (ref var item in color.Data.AsSpan())
                item.G = green;
        }
        public static void SetGreen(this ArrayTextureMap<Rgba32> color, ArrayTextureMap<byte> green)
        {
            color.CheckSameSize(green);
            for (int i = 0; i < color.Data.Length; i++)
                color.Data[i].G = green.Data[i];
        }
        public static void SetBlue(this ArrayTextureMap<Rgba32> color, byte blue)
        {
            foreach (ref var item in color.Data.AsSpan())
                item.B = blue;
        }
        public static void SetBlue(this ArrayTextureMap<Rgba32> color, ArrayTextureMap<byte> blue)
        {
            color.CheckSameSize(blue);
            for (int i = 0; i < color.Data.Length; i++)
                color.Data[i].B = blue.Data[i];
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
            color.CheckSameSize(alpha);

            var w = color.Width;
            var h = color.Height;

            var grey = alpha.GreyAverage();
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

        public static void RemoveTransparentNoise(this ArrayTextureMap<Rgba32> map)
        {
            RemoveTransparentNoise(map.Data);
        }
        private static void RemoveTransparentNoise(Span<Rgba32> data)
        {
            foreach (ref var item in data)
            {
                if (item.A <= 5)
                    item = new Rgba32(0, 0, 0, 0);
            }
        }

        public static ArrayTextureMap<Rgb24> WithBackground(this ArrayTextureMap<Rgba32> map, Rgb24 background)
        {
            var result = new Rgb24[map.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = map[i].WithBackground(background).Rgb;
            return result.AsTextureMap(map.Width);
        }
        public static void SetBackground(this ArrayTextureMap<Rgba32> map, Rgb24 background)
        {
            foreach (ref var p in map.Data.AsSpan())
                p = p.WithBackground(background);
        }

        public static ArrayTextureMap<Rgba32> CombineWithBackground(this ArrayTextureMap<Rgba32> map1, ArrayTextureMap<Rgba32> map2, Rgb24 bg1, Rgb24 bg2)
        {
            map1.CheckSameSize(map2);

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

        public static ArrayTextureMap<P> Blur<P, A>(this ArrayTextureMap<P> map, int radius, AverageAccumulatorFactory<P, A> factory)
            where P : struct
            where A : IAverageAccumulator<P>, new()
        {
            if (radius <= 0) throw new ArgumentException();

            var w = map.Width;
            var h = map.Height;

            var result = new P[w * h];

            for (int y = 0; y < h; y++)
            {
                var yMin = Math.Max(0, y - radius);
                var yMax = Math.Min(h - 1, y + radius);
                for (int x = 0; x < w; x++)
                {
                    var xMin = Math.Max(0, x - radius);
                    var xMax = Math.Min(w - 1, x + radius);

                    var total = factory.Create();
                    for (int i = yMin; i <= yMax; i++)
                        for (int j = xMin; j <= xMax; j++)
                            total.Add(map[i * w + j]);

                    result[y * w + x] = total.Result;
                }
            }

            return result.AsTextureMap(w);
        }

        public static void Min(this ArrayTextureMap<byte> map, ArrayTextureMap<byte> other)
        {
            map.CheckSameSize(other);

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

        public static float GetSharpnessScore(this ArrayTextureMap<Rgba32> map)
        {
            var w = map.Width;
            var h = map.Height;

            long varR = 0;
            long varG = 0;
            long varB = 0;

            static long Sq(long v) => v * v;

            for (var y = 1; y < h; y++)
            {
                for (var x = 1; x < w; x++)
                {
                    var p = map[y * w + x];
                    var px = map[y * w + (x - 1)];
                    var py = map[(y - 1) * w + x];

                    varR += Sq(p.R - px.R);
                    varG += Sq(p.G - px.G);
                    varB += Sq(p.B - px.B);

                    varR += Sq(p.R - py.R);
                    varG += Sq(p.G - py.G);
                    varB += Sq(p.B - py.B);
                }
            }

            var n = (w - 1.0) * (h - 1.0) * 2.0;

            var stdR = Math.Sqrt(varR / n) / 255;
            var stdG = Math.Sqrt(varG / n) / 255;
            var stdB = Math.Sqrt(varB / n) / 255;

            return (float)(stdR * 0.3 + stdG * 0.5 + stdB * 0.2);
        }

        public static void FillSmallHoles(this ArrayTextureMap<Rgba32> map)
        {
            FillHolesAlphaPreprocessing(map);
            var copy = map.Clone();
            FillSmallHolesImpl(map, copy);
        }
        private static void FillSmallHolesImpl(this ArrayTextureMap<Rgba32> map, ArrayTextureMap<Rgba32> workingCopy)
        {
            static void DoIteration(ArrayTextureMap<Rgba32> source, ArrayTextureMap<Rgba32> target)
            {
                var w = source.Width;
                var h = source.Height;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        var s = source.Data[y * w + x];

                        if (s.A == 0)
                        {
                            var avg = new ColorAverage();

                            if (x != 0) avg.Add(ref source.Data[y * w + (x - 1)]);
                            if (x + 1 < w) avg.Add(ref source.Data[y * w + (x + 1)]);
                            if (y != 0) avg.Add(ref source.Data[(y - 1) * w + x]);
                            if (y + 1 < h) avg.Add(ref source.Data[(y + 1) * w + x]);

                            s = avg.GetResult();
                        }

                        target[y * w + x] = s;
                    }
                }
            }

            const int Iterations = 4;
            for (var i = 0; i < Iterations; i++)
            {
                DoIteration(map, workingCopy);
                DoIteration(workingCopy, map);
            }
        }
        private static void FillHolesAlphaPreprocessing(ArrayTextureMap<Rgba32> map)
        {
            const int low = 15;
            foreach (ref var item in map.Data.AsSpan())
            {
                item.A = item.A < low ? (byte)0 : (byte)255;
            }
        }
        private struct ColorAverage
        {
            private int r;
            private int g;
            private int b;
            private int count;

            public void Add(ref Rgba32 color)
            {
                if (color.A != 0)
                {
                    r += color.R;
                    g += color.G;
                    b += color.B;
                    count++;
                }
            }

            public Rgba32 GetResult()
            {
                if (count == 0)
                    return new Rgba32(0, 0, 0, 0);
                else
                    return new Rgba32((byte)(r / count), (byte)(g / count), (byte)(b / count), 255);
            }
        }

        public static void FillSmallHoles2(this ArrayTextureMap<Rgba32> map)
        {
            FillHolesAlphaPreprocessing(map);

            var copy = map.Clone();
            FillSmallHoles2Impl(map, copy);
            FillSmallHolesImpl(map, copy);
        }
        private static void FillSmallHoles2Impl(this ArrayTextureMap<Rgba32> map, ArrayTextureMap<Rgba32> workingCopy)
        {

            static void DoIteration(ArrayTextureMap<Rgba32> source, ArrayTextureMap<Rgba32> target)
            {
                var w = source.Width;
                var h = source.Height;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        var index = y * w + x;
                        var s = source.Data[index];

                        if (s.A == 0)
                        {
                            static void SetIf(ref Rgba32 s, Rgba32[] data, int index)
                            {
                                if (s.A == 0 || index % 3 == 0)
                                {
                                    var color = data[index];
                                    if (color.A != 0) s = color;
                                }
                            }

                            if (x - 2 >= 0) SetIf(ref s, source.Data, y * w + (x - 2));
                            if (x + 2 < w) SetIf(ref s, source.Data, y * w + (x + 2));
                            if (y - 2 >= 0) SetIf(ref s, source.Data, (y - 2) * w + x);
                            if (y + 2 < h) SetIf(ref s, source.Data, (y + 2) * w + x);
                        }

                        target[index] = s;
                    }
                }
            }

            DoIteration(map, workingCopy);
            DoIteration(workingCopy, map);
        }
        public static void FillSmallHoles3(this ArrayTextureMap<Rgba32> map)
        {
            FillHolesAlphaPreprocessing(map);

            var copy = map.Clone();
            FillSmallHoles3Impl(map, copy);
            FillSmallHolesImpl(map, copy);
        }
        private static void FillSmallHoles3Impl(this ArrayTextureMap<Rgba32> map, ArrayTextureMap<Rgba32> workingCopy)
        {
            static Rgba32 FragmentBlur(ArrayTextureMap<Rgba32> source, int x, int y, int n, float distance, float offset)
            {
                var acc = new RgbGammaCorrectedPremultipliedAlphaAverageAccumulator();

                for (int i = 0; i < n; i++)
                {
                    var angle = MathF.PI * 2 * i / n + offset;
                    var xOffset = (int)(MathF.Cos(angle) * distance);
                    var yOffset = (int)(MathF.Sin(angle) * distance);
                    var x_ = x - xOffset;
                    var y_ = y - yOffset;

                    if (x_ >= 0 && y_ >= 0 && x_ < source.Width && y_ < source.Height)
                        acc.Add(source[x_, y_]);
                }

                return acc.Result;
            }
            static void DoIteration(ArrayTextureMap<Rgba32> source, ArrayTextureMap<Rgba32> target)
            {
                var w = source.Width;
                var h = source.Height;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        var index = y * w + x;
                        var s = source.Data[index];

                        if (s.A != 255)
                        {
                            // This implements something called fragment blur.
                            // The idea is to blur an image by offsetting n copies of the image by a distance d. Each
                            // copy is rotated around the center and its alpha is multiplied by 1/n. For a
                            // demonstration of this blur, see the Paint.net effect with the same name.
                            //
                            // I will do a variants, or multiple such blurs layered on top of each other to be more
                            // precise.

                            for (var i = 0; i <= 5; i++)
                            {
                                var b = FragmentBlur(source, x, y, 5, (1 << i), i);
                                s = s.WithBackground(b.WithSelfBackground());
                            }
                        }

                        target[index] = s;
                    }
                }
            }

            DoIteration(map, workingCopy);
            map.Set(workingCopy);
            FillHolesAlphaPreprocessing(map);
            // DoIteration(workingCopy, map);
        }

        public static ArrayTextureMap<T> CobmineTiles<T>(this ArrayTextureMap<T>[,] tiles)
            where T : struct
        {
            var tilesY = tiles.GetLength(0);
            var tilesX = tiles.GetLength(1);

            if (tilesX == 0 || tilesY == 0)
                return new T[0].AsTextureMap(0);

            var width = tiles[0, 0].Width;
            var height = tiles[0, 0].Height;

            var result = new T[width * height * tilesX * tilesY];
            var resultWidth = width * tilesX;

            for (int yT = 0; yT < tilesY; yT++)
            {
                for (int xT = 0; xT < tilesX; xT++)
                {
                    var yOffset = yT * height;
                    var xOffset = xT * width;

                    var source = tiles[yT, xT];
                    if (source.Width != width || source.Height != height)
                        throw new ArgumentException("All tiles have to be the same size");

                    for (int y = 0; y < height; y++)
                        Array.Copy(source.Data, y * width, result, (yOffset + y) * resultWidth + xOffset, width);
                }
            }

            return result.AsTextureMap(resultWidth);
        }

        public static void AddSmoothGreen(this ArrayTextureMap<Rgba32> rough, ArrayTextureMap<Rgba32> smooth)
        {
            rough.CheckSameSize(smooth);

            static float Greenness(float hue)
            {
                const float Low = 40;
                const float Mid1 = 80;
                const float Mid2 = 120;
                const float High = 180;

                if (hue < Low) return 0;
                if (hue < Mid1) return (hue - Low) / (Mid1 - Low);
                if (hue < Mid2) return 1;
                if (hue < High) return 1 - (hue - Mid2) / (High - Mid2);
                return 0;
            }
            static Rgba32 Interpolate(Rgba32 r, Rgba32 s)
            {
                var (hue, sat, v) = (HSV)s;

                var g = Greenness(hue);
                var sat_ = sat.ExtendOut(0.1f, 0.4f);

                return r.Lerp(s, MathF.Sqrt(g * sat_));
            }

            for (int i = 0; i < rough.Data.Length; i++)
            {
                ref var r = ref rough.Data[i];
                r = Interpolate(r, smooth.Data[i]);
            }
        }

        public static (double color, double feature) GetSimilarityScore(this ArrayTextureMap<Rgba32> a, ArrayTextureMap<Rgba32> b)
        {
            MakeSameSizeCopies(ref a, ref b);
            NormalizeChannels(a, b);
            return (
                GetColorSimilarityScoreSameSize(a, b),
                GetColorSimilarityScoreSameSize(a.Sobel(), b.Sobel())
            );
        }
        public static double GetColorSimilarityScore(this ArrayTextureMap<Rgba32> a, ArrayTextureMap<Rgba32> b)
        {
            MakeSameSizeCopies(ref a, ref b);
            NormalizeChannels(a, b);
            return GetColorSimilarityScoreSameSize(a, b);
        }
        public static double GetFeatureSimilarityScore(this ArrayTextureMap<Rgba32> a, ArrayTextureMap<Rgba32> b)
        {
            MakeSameSizeCopies(ref a, ref b);
            NormalizeChannels(a, b);
            return GetColorSimilarityScoreSameSize(a.Sobel(), b.Sobel());
        }
        private static void MakeSameSizeCopies(ref ArrayTextureMap<Rgba32> a, ref ArrayTextureMap<Rgba32> b)
        {
            if (a.Width != b.Width || a.Height != b.Height)
            {
                if (!SizeRatio.Of(a).Equals(SizeRatio.Of(b)))
                    throw new Exception("The two images have different size ratios.");

                // Make sure a is the larger image
                if (a.Width < b.Width) (a, b) = (b, a);

                var f = a.Width / b.Width;
                if (b.Width * f != a.Width)
                    throw new Exception("Cannot resize using whole-number scaling factor.");

                // resize the larger one to the smaller size
                a = a.DownSample(Average.Rgba32, f);
                b = b.Clone();
            }
            else
            {
                a = a.Clone();
                b = b.Clone();
            }
        }
        private static void NormalizeChannels(ArrayTextureMap<Rgba32> a, ArrayTextureMap<Rgba32> b)
        {
            var min = new Rgba32(255, 255, 255, 255);
            var max = new Rgba32(0, 0, 0, 0);

            foreach (var p in a.Data)
            {
                min.R = Math.Min(min.R, p.R);
                min.G = Math.Min(min.G, p.G);
                min.B = Math.Min(min.B, p.B);
                min.A = Math.Min(min.A, p.A);
                max.R = Math.Max(max.R, p.R);
                max.G = Math.Max(max.G, p.G);
                max.B = Math.Max(max.B, p.B);
                max.A = Math.Max(max.A, p.A);
            }
            foreach (var p in b.Data)
            {
                min.R = Math.Min(min.R, p.R);
                min.G = Math.Min(min.G, p.G);
                min.B = Math.Min(min.B, p.B);
                min.A = Math.Min(min.A, p.A);
                max.R = Math.Max(max.R, p.R);
                max.G = Math.Max(max.G, p.G);
                max.B = Math.Max(max.B, p.B);
                max.A = Math.Max(max.A, p.A);
            }

            static void Adjust(ref byte min, ref byte max)
            {
                var diff = max - min;
                if (diff <= 0)
                {
                    min = 0;
                    max = 255;
                    return;
                }

                var mid = (max + min) / 2;

                const int MaxStep = 8;
                var maxRange = Math.Min(255, diff * MaxStep);

                var low = Math.Max(0, mid - maxRange / 2);
                var high = Math.Min(255, low + maxRange);
                low = high - maxRange;

                var d = (byte)(255 * diff / (double)maxRange);
                var m = min - low * d / 255;

                min = (byte)Math.Max(0, m);
                max = (byte)Math.Min(255, min + d);
            }
            Adjust(ref min.R, ref max.R);
            Adjust(ref min.G, ref max.G);
            Adjust(ref min.B, ref max.B);
            Adjust(ref min.A, ref max.A);

            foreach (ref var p in a.Data.AsSpan())
            {
                p.R = (byte)((p.R - min.R) * 255 / (max.R - min.R));
                p.G = (byte)((p.G - min.G) * 255 / (max.G - min.G));
                p.B = (byte)((p.B - min.B) * 255 / (max.B - min.B));
                p.A = (byte)((p.A - min.A) * 255 / (max.A - min.A));
            }
            foreach (ref var p in b.Data.AsSpan())
            {
                p.R = (byte)((p.R - min.R) * 255 / (max.R - min.R));
                p.G = (byte)((p.G - min.G) * 255 / (max.G - min.G));
                p.B = (byte)((p.B - min.B) * 255 / (max.B - min.B));
                p.A = (byte)((p.A - min.A) * 255 / (max.A - min.A));
            }
        }
        private static double GetColorSimilarityScoreSameSize(this ArrayTextureMap<Rgba32> mapA, ArrayTextureMap<Rgba32> mapB)
        {
            CheckSameSize(mapA, mapB);

            long r = 0;
            long g = 0;
            long b = 0;
            long a = 0;

            for (int i = 0; i < mapA.Data.Length; i++)
            {
                var pA = mapA.Data[i];
                var pB = mapB.Data[i];

                r += Math.Abs(pA.R - pB.R);
                g += Math.Abs(pA.G - pB.G);
                b += Math.Abs(pA.B - pB.B);
                a += Math.Abs(pA.A - pB.A);
            }

            return Math.Sqrt(r * r + g * g + b * b + a * a) / mapA.Data.Length / 510;
        }

        public static ArrayTextureMap<Rgba32> Sobel(this ArrayTextureMap<Rgba32> map)
        {
            var w = map.Width;
            var h = map.Height;

            var result = new Rgba32[map.Count].AsTextureMap(w);
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    var gx = new RgbaInt();
                    gx.Add(map[x - 1, y - 1], 1);
                    gx.Add(map[x - 1, y + 0], 2);
                    gx.Add(map[x - 1, y + 1], 1);
                    gx.Add(map[x + 1, y - 1], -1);
                    gx.Add(map[x + 1, y + 0], -2);
                    gx.Add(map[x + 1, y + 1], -1);

                    var gy = new RgbaInt();
                    gy.Add(map[x - 1, y - 1], 1);
                    gy.Add(map[x + 0, y - 1], 2);
                    gy.Add(map[x + 1, y - 1], 1);
                    gy.Add(map[x - 1, y + 1], -1);
                    gy.Add(map[x + 0, y + 1], -2);
                    gy.Add(map[x + 1, y + 1], -1);

                    var mag = new Rgba32();
                    mag.R = (byte)Math.Min((int)MathF.Sqrt(gx.R * gx.R + gy.R * gy.R), 255);
                    mag.G = (byte)Math.Min((int)MathF.Sqrt(gx.G * gx.G + gy.G * gy.G), 255);
                    mag.B = (byte)Math.Min((int)MathF.Sqrt(gx.B * gx.B + gy.B * gy.B), 255);
                    mag.A = (byte)Math.Min((int)MathF.Sqrt(gx.A * gx.A + gy.A * gy.A), 255);

                    result[x, y] = mag;
                }
            }

            return result;
        }
        private struct RgbaInt
        {
            public int R;
            public int G;
            public int B;
            public int A;

            public void Add(Rgba32 c, int factor)
            {
                R += c.R * factor;
                G += c.G * factor;
                B += c.B * factor;
                A += c.A * factor;
            }
        }


        public static ArrayTextureMap<Rgba32> UpSample(this ArrayTextureMap<Rgba32> source, int scale)
        {
            var sW = source.Width;
            var sH = source.Height;

            var result = new Rgba32[sW * scale * sH * scale].AsTextureMap(sW * scale);
            var rW = sW * scale;
            var rH = sH * scale;

            for (var y = 0; y < rH; y++)
            {
                for (var x = 0; x < rW; x++)
                {
                    var xs = (x + .5) / scale - .5;
                    var ys = (y + .5) / scale - .5;
                    var xsFloor = (int)Math.Floor(xs);
                    var ysFloor = (int)Math.Floor(ys);

                    var xBlend = (float)(xs - xsFloor);
                    var yBlend = (float)(ys - ysFloor);
                    var x0 = Math.Max(0, xsFloor - 1);
                    var x1 = Math.Max(0, xsFloor);
                    var x2 = Math.Min(sW - 1, xsFloor + 1);
                    var x3 = Math.Min(sW - 1, xsFloor + 2);
                    var y0 = Math.Max(0, ysFloor - 1);
                    var y1 = Math.Max(0, ysFloor);
                    var y2 = Math.Min(sH - 1, ysFloor + 1);
                    var y3 = Math.Min(sH - 1, ysFloor + 2);

                    // Interpolate the angle, not the normals
                    var a00 = source[x0, y0];
                    var a01 = source[x0, y1];
                    var a02 = source[x0, y2];
                    var a03 = source[x0, y3];
                    var a10 = source[x1, y0];
                    var a11 = source[x1, y1];
                    var a12 = source[x1, y2];
                    var a13 = source[x1, y3];
                    var a20 = source[x2, y0];
                    var a21 = source[x2, y1];
                    var a22 = source[x2, y2];
                    var a23 = source[x2, y3];
                    var a30 = source[x3, y0];
                    var a31 = source[x3, y1];
                    var a32 = source[x3, y2];
                    var a33 = source[x3, y3];

                    var r = Cubic(
                        Cubic(a00.R, a10.R, a20.R, a30.R, xBlend),
                        Cubic(a01.R, a11.R, a21.R, a31.R, xBlend),
                        Cubic(a02.R, a12.R, a22.R, a32.R, xBlend),
                        Cubic(a03.R, a13.R, a23.R, a33.R, xBlend),
                        yBlend
                    );
                    var g = Cubic(
                        Cubic(a00.G, a10.G, a20.G, a30.G, xBlend),
                        Cubic(a01.G, a11.G, a21.G, a31.G, xBlend),
                        Cubic(a02.G, a12.G, a22.G, a32.G, xBlend),
                        Cubic(a03.G, a13.G, a23.G, a33.G, xBlend),
                        yBlend
                    );
                    var b = Cubic(
                        Cubic(a00.B, a10.B, a20.B, a30.B, xBlend),
                        Cubic(a01.B, a11.B, a21.B, a31.B, xBlend),
                        Cubic(a02.B, a12.B, a22.B, a32.B, xBlend),
                        Cubic(a03.B, a13.B, a23.B, a33.B, xBlend),
                        yBlend
                    );
                    var a = Cubic(
                        Cubic(a00.A, a10.A, a20.A, a30.A, xBlend),
                        Cubic(a01.A, a11.A, a21.A, a31.A, xBlend),
                        Cubic(a02.A, a12.A, a22.A, a32.A, xBlend),
                        Cubic(a03.A, a13.A, a23.A, a33.A, xBlend),
                        yBlend
                    );

                    static byte ToByte(float v) => (byte)Math.Clamp((int)v, 0, 255);
                    result[x, y] = new Rgba32(ToByte(r), ToByte(g), ToByte(b), ToByte(a));
                }
            }

            return result;
        }
        public static ArrayTextureMap<byte> UpSample(this ArrayTextureMap<byte> source, int scale)
        {
            var sW = source.Width;
            var sH = source.Height;

            var result = new byte[sW * scale * sH * scale].AsTextureMap(sW * scale);
            var rW = sW * scale;
            var rH = sH * scale;

            for (var y = 0; y < rH; y++)
            {
                for (var x = 0; x < rW; x++)
                {
                    var xs = (x + .5) / scale - .5;
                    var ys = (y + .5) / scale - .5;
                    var xsFloor = (int)Math.Floor(xs);
                    var ysFloor = (int)Math.Floor(ys);

                    var xBlend = (float)(xs - xsFloor);
                    var yBlend = (float)(ys - ysFloor);
                    var x0 = Math.Max(0, xsFloor - 1);
                    var x1 = Math.Max(0, xsFloor);
                    var x2 = Math.Min(sW - 1, xsFloor + 1);
                    var x3 = Math.Min(sW - 1, xsFloor + 2);
                    var y0 = Math.Max(0, ysFloor - 1);
                    var y1 = Math.Max(0, ysFloor);
                    var y2 = Math.Min(sH - 1, ysFloor + 1);
                    var y3 = Math.Min(sH - 1, ysFloor + 2);

                    // Interpolate the angle, not the normals
                    var a00 = source[x0, y0];
                    var a01 = source[x0, y1];
                    var a02 = source[x0, y2];
                    var a03 = source[x0, y3];
                    var a10 = source[x1, y0];
                    var a11 = source[x1, y1];
                    var a12 = source[x1, y2];
                    var a13 = source[x1, y3];
                    var a20 = source[x2, y0];
                    var a21 = source[x2, y1];
                    var a22 = source[x2, y2];
                    var a23 = source[x2, y3];
                    var a30 = source[x3, y0];
                    var a31 = source[x3, y1];
                    var a32 = source[x3, y2];
                    var a33 = source[x3, y3];

                    var v = Cubic(
                        Cubic(a00, a10, a20, a30, xBlend),
                        Cubic(a01, a11, a21, a31, xBlend),
                        Cubic(a02, a12, a22, a32, xBlend),
                        Cubic(a03, a13, a23, a33, xBlend),
                        yBlend
                    );

                    static byte ToByte(float v) => (byte)Math.Clamp((int)v, 0, 255);
                    result[x, y] = ToByte(v);
                }
            }

            return result;
        }
        public static ArrayTextureMap<float> UpSample(this ArrayTextureMap<float> source, int scale)
        {
            var sW = source.Width;
            var sH = source.Height;

            var result = new float[sW * scale * sH * scale].AsTextureMap(sW * scale);
            var rW = sW * scale;
            var rH = sH * scale;

            for (var y = 0; y < rH; y++)
            {
                for (var x = 0; x < rW; x++)
                {
                    var xs = (x + .5) / scale - .5;
                    var ys = (y + .5) / scale - .5;
                    var xsFloor = (int)Math.Floor(xs);
                    var ysFloor = (int)Math.Floor(ys);

                    var xBlend = (float)(xs - xsFloor);
                    var yBlend = (float)(ys - ysFloor);
                    var x0 = Math.Max(0, xsFloor - 1);
                    var x1 = Math.Max(0, xsFloor);
                    var x2 = Math.Min(sW - 1, xsFloor + 1);
                    var x3 = Math.Min(sW - 1, xsFloor + 2);
                    var y0 = Math.Max(0, ysFloor - 1);
                    var y1 = Math.Max(0, ysFloor);
                    var y2 = Math.Min(sH - 1, ysFloor + 1);
                    var y3 = Math.Min(sH - 1, ysFloor + 2);

                    var a00 = source[x0, y0];
                    var a01 = source[x0, y1];
                    var a02 = source[x0, y2];
                    var a03 = source[x0, y3];
                    var a10 = source[x1, y0];
                    var a11 = source[x1, y1];
                    var a12 = source[x1, y2];
                    var a13 = source[x1, y3];
                    var a20 = source[x2, y0];
                    var a21 = source[x2, y1];
                    var a22 = source[x2, y2];
                    var a23 = source[x2, y3];
                    var a30 = source[x3, y0];
                    var a31 = source[x3, y1];
                    var a32 = source[x3, y2];
                    var a33 = source[x3, y3];

                    var v = Cubic(
                        Cubic(a00, a10, a20, a30, xBlend),
                        Cubic(a01, a11, a21, a31, xBlend),
                        Cubic(a02, a12, a22, a32, xBlend),
                        Cubic(a03, a13, a23, a33, xBlend),
                        yBlend
                    );

                    result[x, y] = v;
                }
            }

            return result;
        }
        public static ArrayTextureMap<Normal> UpSampleNormals(this ITextureMap<Normal> map, int scale)
        {
            var source = map.Convert(NormalAngle.FromNormal);
            var sW = source.Width;
            var sH = source.Height;

            var result = new Normal[sW * scale * sH * scale].AsTextureMap(sW * scale);
            var rW = sW * scale;
            var rH = sH * scale;

            for (var y = 0; y < rH; y++)
            {
                for (var x = 0; x < rW; x++)
                {
                    var xs = (x + .5) / scale - .5;
                    var ys = (y + .5) / scale - .5;
                    var xsFloor = (int)Math.Floor(xs);
                    var ysFloor = (int)Math.Floor(ys);

                    var xBlend = (float)(xs - xsFloor);
                    var yBlend = (float)(ys - ysFloor);
                    var x0 = Math.Max(0, xsFloor - 1);
                    var x1 = Math.Max(0, xsFloor);
                    var x2 = Math.Min(sW - 1, xsFloor + 1);
                    var x3 = Math.Min(sW - 1, xsFloor + 2);
                    var y0 = Math.Max(0, ysFloor - 1);
                    var y1 = Math.Max(0, ysFloor);
                    var y2 = Math.Min(sH - 1, ysFloor + 1);
                    var y3 = Math.Min(sH - 1, ysFloor + 2);

                    // Interpolate the angle, not the normals
                    var a00 = source[x0, y0];
                    var a01 = source[x0, y1];
                    var a02 = source[x0, y2];
                    var a03 = source[x0, y3];
                    var a10 = source[x1, y0];
                    var a11 = source[x1, y1];
                    var a12 = source[x1, y2];
                    var a13 = source[x1, y3];
                    var a20 = source[x2, y0];
                    var a21 = source[x2, y1];
                    var a22 = source[x2, y2];
                    var a23 = source[x2, y3];
                    var a30 = source[x3, y0];
                    var a31 = source[x3, y1];
                    var a32 = source[x3, y2];
                    var a33 = source[x3, y3];

                    var aX = Cubic(
                        Cubic(a00.X, a10.X, a20.X, a30.X, xBlend),
                        Cubic(a01.X, a11.X, a21.X, a31.X, xBlend),
                        Cubic(a02.X, a12.X, a22.X, a32.X, xBlend),
                        Cubic(a03.X, a13.X, a23.X, a33.X, xBlend),
                        yBlend
                    );
                    var aY = Cubic(
                        Cubic(a00.Y, a10.Y, a20.Y, a30.Y, xBlend),
                        Cubic(a01.Y, a11.Y, a21.Y, a31.Y, xBlend),
                        Cubic(a02.Y, a12.Y, a22.Y, a32.Y, xBlend),
                        Cubic(a03.Y, a13.Y, a23.Y, a33.Y, xBlend),
                        yBlend
                    );

                    result[x, y] = new NormalAngle(aX, aY);
                }
            }

            return result;
        }
        private static float Cubic(float v0, float v1, float v2, float v3, float blend)
        {
            // Cubic spline interpolation
            float a = (-v0 + 3 * v1 - 3 * v2 + v3) * .5f;
            float b = v0 + (-5 * v1 + 4 * v2 - v3) * .5f;
            float c = (v2 - v0) * .5f;
            float d = v1;

            return d + blend * (c + blend * (b + blend * a));
        }

        public static void AddColorCode(this ArrayTextureMap<Rgba32> map, Span<Rgba32> code)
        {
            static bool IsEven(int i) => (i & 1) != 0;
            static bool Flip(int x, int y, int l)
            {
                x >>= 1;
                y >>= 1;
                return IsEven(y / l) ^ IsEven(x / l);
            }

            // horizontal lines
            for (var y = 0; y < map.Height; y += code.Length * 2)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    if ((x & 1) == 0)
                    {
                        // black
                        map[x, y] = new Rgba32(0, 0, 0, 255);
                    }
                    else
                    {
                        var i = (x >> 1) % code.Length;
                        if (Flip(x, y, code.Length)) i = code.Length - 1 - i;
                        map[x, y] = code[i];
                    }
                }
            }

            // vertical lines
            for (var x = 0; x < map.Width; x += code.Length * 2)
            {
                for (var y = 0; y < map.Height; y++)
                {
                    if ((y & 1) == 0)
                    {
                        // black
                        map[x, y] = new Rgba32(0, 0, 0, 255);
                    }
                    else
                    {
                        var i = (y >> 1) % code.Length;
                        if (Flip(x, y, code.Length)) i = code.Length - 1 - i;
                        map[x, y] = code[i];
                    }
                }
            }
        }

        public static void Multiply(this ArrayTextureMap<Rgba32> map, Rgba32 color)
        {
            foreach (ref var p in map.Data.AsSpan())
            {
                p.R = (byte)(p.R * color.R / 255);
                p.G = (byte)(p.G * color.G / 255);
                p.B = (byte)(p.B * color.B / 255);
                p.A = (byte)(p.A * color.A / 255);
            }
        }

        public static double BCArtifactsScore(this ArrayTextureMap<byte> map)
        {
            // The idea is that BC artifacts are most noticeable on the edges between blocks.
            // This algorithm will calculate the average difference of adjacent pixels in neighboring blocks

            long diff = 0;

            // horizontal
            for (int y = 4; y < map.Height; y += 4)
                for (int x = 0; x < map.Width; x++)
                    diff += Math.Abs(map[x, y - 1] - map[x, y]);
            // vertical
            for (int y = 0; y < map.Height; y++)
                for (int x = 4; x < map.Width; x += 4)
                    diff += Math.Abs(map[x - 1, y] - map[x, y]);

            double count = (map.Width - 1) * (map.Height - 1) - 1;
            return diff / count / 255;
        }

        public static void QuantizeBinary(this ArrayTextureMap<byte> map, byte threshold = 127)
        {
            foreach (ref var p in map.Data.AsSpan())
                p = p > threshold ? (byte)255 : (byte)0;
        }

        public static ArrayTextureMap<T> GetCut<T>(this ArrayTextureMap<T> map, int x, int y, int width, int height)
            where T : struct
        {
            var result = new T[width * height];
            var input = map.Data;

            for (int i = 0; i < height; i++)
                Array.Copy(input, (y + i) * map.Width + x, result, i * width, width);

            return result.AsTextureMap(width);
        }

        public static void CopyBrightnessAndSaturationFrom(this ArrayTextureMap<Rgba32> map, ArrayTextureMap<Rgba32> source, int mapBlurRadius = 0, int sourceBlurRadius = 0)
        {
            var factor = map.Width / source.Width;

            var sourceS = new float[source.Count].AsTextureMap(source.Width);
            var sourceV = new float[source.Count].AsTextureMap(source.Width);
            for (int i = 0; i < source.Count; i++)
            {
                HSV c = source[i].Rgb;
                sourceS[i] = c.S;
                sourceV[i] = c.V;
            }

            if (factor != 1)
            {
                sourceS = sourceS.UpSample(factor);
                sourceV = sourceV.UpSample(factor);
            }
            CheckSameSize(map, sourceS);

            var mapSmall = factor == 1 ? map : map.DownSample(Average.Rgba32GammaAlpha, factor);
            var mapS = new float[mapSmall.Count].AsTextureMap(mapSmall.Width);
            var mapV = new float[mapSmall.Count].AsTextureMap(mapSmall.Width);
            for (int i = 0; i < mapSmall.Count; i++)
            {
                HSV c = mapSmall[i].Rgb;
                mapS[i] = c.S;
                mapV[i] = c.V;
            }

            if (factor != 1)
            {
                mapS = mapS.UpSample(factor);
                mapV = mapV.UpSample(factor);
            }

            if (mapBlurRadius > 0)
            {
                mapS = mapS.Blur(mapBlurRadius, Average.Float);
                mapV = mapV.Blur(mapBlurRadius, Average.Float);
            }
            if (sourceBlurRadius > 0)
            {
                sourceS = sourceS.Blur(sourceBlurRadius, Average.Float);
                sourceV = sourceV.Blur(sourceBlurRadius, Average.Float);
            }


            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var p = map[x, y];
                    HSV c = p.Rgb;
                    c.S = Math.Clamp((c.S + sourceS[x, y] - mapS[x, y]) * c.V.ExtendOut(0, 0.15f), 0, 1);
                    c.V = Math.Clamp(c.V + sourceV[x, y] - mapV[x, y], 0, 1);
                    p.Rgb = c;
                    map[x, y] = p;
                }
            }
        }
        public static void CopyColorFrom(this ArrayTextureMap<Rgba32> map, ArrayTextureMap<Rgba32> source)
        {
            var factor = map.Width / source.Width;

            if (factor < 2) {
                throw new Exception("The source image has to be smaller than this image");
            }

            var sourceRgb = source.UpSample(factor);
            CheckSameSize(map, sourceRgb);

            var mapRgb = map.DownSample(Average.Rgba32GammaAlpha, factor).UpSample(factor);

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var p = map[x, y];

                    p.R = (byte)Math.Clamp(p.R + sourceRgb[x, y].R - mapRgb[x, y].R, 0, 255);
                    p.G = (byte)Math.Clamp(p.G + sourceRgb[x, y].G - mapRgb[x, y].G, 0, 255);
                    p.B = (byte)Math.Clamp(p.B + sourceRgb[x, y].B - mapRgb[x, y].B, 0, 255);

                    map[x, y] = p;
                }
            }
        }
        private static T Sample<T>(this ArrayTextureMap<T> map, int x, int y)
            where T : struct
        {
            return map[Math.Clamp(x, 0, map.Width - 1), Math.Clamp(y, 0, map.Height - 1)];
        }
    }
}
