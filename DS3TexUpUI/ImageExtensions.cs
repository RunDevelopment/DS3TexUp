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
                    image.ProcessPixelRows(accessor =>
                    {
                        var row = accessor.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                            data[stride + x] = map(row[x]);
                    });

                }
                return data;
            }

            var data = image switch
            {
                Image<Bgr24> i => MapRows(i, p => new Rgba32(p.R, p.G, p.B, 255)),
                Image<Bgra32> i => MapRows(i, p => new Rgba32(p.R, p.G, p.B, p.A)),
                Image<Rgba32> i => MapRows(i, p => p),
                Image<Rgb24> i => MapRows(i, p => new Rgba32(p.R, p.G, p.B, 255)),
                Image<Rgb48> i => MapRows(i, p => new Rgba32(p.R / 65535.0f, p.G / 65535.0f, p.B / 65535.0f)),
                Image<Rgba64> i => MapRows(i, p => new Rgba32(p.R / 65535.0f, p.G / 65535.0f, p.B / 65535.0f, p.A / 65535.0f)),
                Image<L8> i => MapRows(i, p => new Rgba32(p.PackedValue, p.PackedValue, p.PackedValue, 255)),
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

        public static ArrayTextureMap<T> Crop<T>(this ArrayTextureMap<T> image, int left, int top, int width, int height)
            where T : struct
        {
            if (left < 0 || top < 0 || width < 0 || height < 0)
                throw new ArgumentException();
            if (left + width > image.Width || top + height > image.Height)
                throw new ArgumentException();

            var result = new T[width * height];

            for (int i = 0; i < height; i++)
            {
                Array.Copy(image.Data, left + (top + i) * image.Width, result, i * width, width);
            }

            return result.AsTextureMap(width);
        }

        public static ArrayTextureMap<T> Tile<T>(this ArrayTextureMap<T> image, int x, int y)
            where T : struct
        {
            if (x <= 0 || y <= 0)
                throw new ArgumentException("Expected tile x and y to be at least 1");

            var resultWidth = image.Width * x;
            var result = new T[image.Width * x * image.Height * y];

            for (int tileX = 0; tileX < x; tileX++)
            {
                for (int tileY = 0; tileY < y; tileY++)
                {
                    for (int i = 0; i < image.Height; i++)
                    {
                        Array.Copy(
                            image.Data,
                            i * image.Width,
                            result,
                            (i + tileY * image.Height) * resultWidth + tileX * image.Width,
                            image.Width
                        );
                    }
                }
            }

            return result.AsTextureMap(resultWidth);
        }

        public static ArrayTextureMap<T> CombineTiles<T>(this ArrayTextureMap<T>[,] tiles)
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
        public static ArrayTextureMap<float> GetMossMap(this ArrayTextureMap<Rgba32> image)
        {
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

            var data = new float[image.Count];
            for (int i = 0; i < image.Data.Length; i++)
            {
                var (hue, sat, v) = (HSV)image.Data[i];

                var g = Greenness(hue);
                var sat_ = sat.ExtendOut(0.1f, 0.4f);

                data[i] = MathF.Sqrt(g * sat_);
            }

            return data.AsTextureMap(image.Width);
        }


        public static RgbaDiff GetMaxAbsDiff(this ArrayTextureMap<Rgba32> image)
        {
            byte minR = 255;
            byte maxR = 0;
            byte minG = 255;
            byte maxG = 0;
            byte minB = 255;
            byte maxB = 0;
            byte minA = 255;
            byte maxA = 0;

            for (int y = 1; y < image.Height - 1; y++)
            {
                for (int x = 1; x < image.Width - 1; x++)
                {
                    var c = image[x, y];
                    minR = c.R < minR ? c.R : minR;
                    maxR = c.R > maxR ? c.R : maxR;
                    minG = c.G < minG ? c.G : minG;
                    maxG = c.G > maxG ? c.G : maxG;
                    minB = c.B < minB ? c.B : minB;
                    maxB = c.B > maxB ? c.B : maxB;
                    minA = c.A < minA ? c.A : minA;
                    maxA = c.A > maxA ? c.A : maxA;
                }
            }

            var dR = (byte)(maxR - minR);
            var dG = (byte)(maxG - minG);
            var dB = (byte)(maxB - minB);
            var dA = (byte)(maxA - minA);

            return new RgbaDiff(dR, dG, dB, dA);
        }
        public static TransparencyKind GetTransparency(this ArrayTextureMap<Rgba32> image)
        {
            var map = new bool[256];
            foreach (ref var p in image.Data.AsSpan())
            {
                map[p.A] = true;
            }

            var min = 255;
            for (int i = 0; i < 256; i++)
            {
                if (map[i])
                {
                    min = i;
                    break;
                }
            }

            if (min == 255) return TransparencyKind.None;
            if (min >= 250) return TransparencyKind.Unnoticeable;

            var hasMin = false;
            for (int i = 6; i < 250; i++)
            {
                if (map[i])
                {
                    hasMin = true;
                    break;
                }
            }

            return hasMin ? TransparencyKind.Full : TransparencyKind.Binary;
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

        public static ArrayTextureMap<P> UpSample<P, I, S>(this ITextureMap<P> map, int scale, BiCubicFactory<P, I, S> factory)
                    where P : struct
                    where I : struct
                    where S : IBiCubicSample<P, I>, new()
        {
            return UpSample(map is ArrayTextureMap<P> arrayMap ? arrayMap : map.Clone(), scale, factory);
        }
        public static ArrayTextureMap<P> UpSample<P, I, S>(this ArrayTextureMap<P> map, int scale, BiCubicFactory<P, I, S> factory)
            where P : struct
            where I : struct
            where S : IBiCubicSample<P, I>, new()
        {
            if (scale < 2) throw new ArgumentOutOfRangeException(nameof(scale));
            if (scale % 2 != 0) throw new ArgumentOutOfRangeException(nameof(scale));

            var source = factory.Preprocess(map);

            var sW = source.Width;
            var sH = source.Height;

            var result = new P[sW * scale * sH * scale].AsTextureMap(sW * scale);
            var scaleHalf = scale / 2;

            var sample = factory.createSample();

            for (var y = -1; y < sH; y++)
            {
                for (var x = -1; x < sW; x++)
                {
                    var x0 = Math.Max(0, x - 1);
                    var x1 = Math.Max(0, x);
                    var x2 = Math.Min(sW - 1, x + 1);
                    var x3 = Math.Min(sW - 1, x + 2);
                    var y0 = Math.Max(0, y - 1);
                    var y1 = Math.Max(0, y);
                    var y2 = Math.Min(sH - 1, y + 1);
                    var y3 = Math.Min(sH - 1, y + 2);

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

                    sample.Assign(
                        a00, a10, a20, a30,
                        a01, a11, a21, a31,
                        a02, a12, a22, a32,
                        a03, a13, a23, a33
                    );

                    var rXMin = x * scale + scaleHalf;
                    var rYMin = y * scale + scaleHalf;

                    var rXStart = Math.Max(0, rXMin);
                    var rXEnd = Math.Min(result.Width, rXMin + scale);
                    var rYStart = Math.Max(0, rYMin);
                    var rYEnd = Math.Min(result.Height, rYMin + scale);

                    for (int rY = rYStart; rY < rYEnd; rY++)
                    {
                        for (int rX = rXStart; rX < rXEnd; rX++)
                        {
                            // 0 <= i,j < scale
                            var i = rX - rXMin;
                            var j = rY - rYMin;

                            var xBlend = (i + 0.5f) / scale;
                            var yBlend = (j + 0.5f) / scale;

                            result[rX, rY] = sample.Interpolate(xBlend, yBlend);
                        }
                    }
                }
            }

            return result;
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
                sourceS = sourceS.UpSample(factor, BiCubic.Float);
                sourceV = sourceV.UpSample(factor, BiCubic.Float);
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
                mapS = mapS.UpSample(factor, BiCubic.Float);
                mapV = mapV.UpSample(factor, BiCubic.Float);
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
        public static void CopyColorFrom(this ArrayTextureMap<Rgba32> map, ArrayTextureMap<Rgba32> source, int downScale = 1)
        {
            if (downScale < 1) throw new ArgumentOutOfRangeException(nameof(downScale));
            if (downScale > 1) source = source.DownSample(Average.Rgba32, downScale);

            var factor = map.Width / source.Width;
            if (factor < 2) throw new Exception("The source image has to be smaller than this image");

            var sourceRgb = source.UpSample(factor, BiCubic.Rgba);
            CheckSameSize(map, sourceRgb);

            var mapRgb = map.DownSample(Average.Rgba32, factor).UpSample(factor, BiCubic.Rgba);

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

        public static ArrayTextureMap<Rgba32> GetHighFreqOverlay(this ArrayTextureMap<Rgba32> image, int down, int blur)
        {
            var low = image;
            if (down > 1) low = low.DownSample(Average.Rgba32, down);
            if (blur > 0) low = low.Blur(blur, Average.Rgba32);
            if (down > 1) low = low.UpSample(down, BiCubic.Rgba);

            for (int i = 0; i < low.Data.Length; i++)
            {
                ref var p = ref low.Data[i];
                var c = image.Data[i];
                p.R = (byte)(127 + c.R - p.R);
                p.G = (byte)(127 + c.G - p.G);
                p.B = (byte)(127 + c.B - p.B);
                p.A = 255;
            }

            return low;
        }

        public static ArrayTextureMap<Rgba32> GetDifferenceAdditiveOverlay(this ArrayTextureMap<Rgba32> image, ArrayTextureMap<Rgba32> final)
        {
            if (image.Width > final.Width)
                image = image.DownSample(Average.Rgba32, image.Width / final.Width);

            if (image.Width != final.Width || image.Height != final.Height)
                throw new ArgumentException("Image sizes are not compatible.");

            var result = new Rgba32[image.Count];
            for (int i = 0; i < image.Data.Length; i++)
            {
                var b = image.Data[i];
                var f = final.Data[i];

                result[i] = new Rgba32(
                    (byte)(127 + (f.R - b.R) / 2),
                    (byte)(127 + (f.G - b.G) / 2),
                    (byte)(127 + (f.B - b.B) / 2)
                );
            }

            return result.AsTextureMap(image.Width);
        }
        public static ArrayTextureMap<Rgba32> GetDifferenceMultiplicativeOverlay(this ArrayTextureMap<Rgba32> image, ArrayTextureMap<Rgba32> final)
        {
            if (image.Width > final.Width)
                image = image.DownSample(Average.Rgba32, image.Width / final.Width);

            if (image.Width != final.Width || image.Height != final.Height)
                throw new ArgumentException("Image sizes are not compatible.");

            var result = new Rgba32[image.Count];
            for (int i = 0; i < image.Data.Length; i++)
            {
                var b = image.Data[i];
                var f = final.Data[i];

                static byte GetOverlay(byte b, byte f)
                {
                    if (f == b) return 127;
                    if (f < b) return (byte)(255 * f / (2 * b));
                    var bf = b / 255f;
                    var ff = f / 255f;
                    return (byte)Math.Round(255 * (1 - (1 - ff) / (2 * (1 - bf))));
                }

                result[i] = new Rgba32(
                    GetOverlay(b.R, f.R),
                    GetOverlay(b.G, f.G),
                    GetOverlay(b.B, f.B)
                );
            }

            return result.AsTextureMap(image.Width);
        }
    }
}
