using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DS3TexUpUI
{
    public interface ITextureMap<T> : IReadOnlyCollection<T>
        where T : struct
    {
        int Width { get; }
        int Height { get; }

        T this[int index] { get; set; }
        T this[int x, int y] { get; set; }
    }

    /// <summary>A texture map backed by an array.</summary>
    public readonly struct ArrayTextureMap<T> : ITextureMap<T>
        where T : struct
    {
        public readonly T[] Data;

        public int Width { get; }
        public int Height { get; }
        public int Count => Width * Height;

        public T this[int index] { get => Data[index]; set => Data[index] = value; }
        public T this[int x, int y] { get => this[y * Width + x]; set => this[y * Width + x] = value; }

        public ArrayTextureMap(T[] data, int width, int height)
        {
            Width = width;
            Height = height;
            Data = data;
        }

        public ArrayTextureMap<T> Clone()
        {
            var copy = new T[Count];
            Array.Copy(Data, copy, copy.Length);
            return new ArrayTextureMap<T>(copy, Width, Height);
        }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var item in Data)
                yield return item;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class TextureMapExtensions
    {
        public static void Set<T>(this ITextureMap<T> map, ITextureMap<T> source)
            where T : struct
        {
            map.CheckSameSize(source);

            var count = map.Count;
            for (int i = 0; i < count; i++)
                map[i] = source[i];
        }
        public static void Set<T>(this ITextureMap<T> map, Span<T> source)
            where T : struct
        {
            var count = map.Count;
            for (int i = 0; i < count; i++)
                map[i] = source[i];
        }
        public static void Set<T>(this ITextureMap<T> map, T source)
            where T : struct
        {
            var count = map.Count;
            for (int i = 0; i < count; i++)
                map[i] = source;
        }

        public static ArrayTextureMap<O> Convert<T, O>(this ITextureMap<T> map, Func<T, O> converter)
            where T : struct
            where O : struct
        {
            var data = new O[map.Count];
            for (int i = 0; i < data.Length; i++)
                data[i] = converter(map[i]);

            return new ArrayTextureMap<O>(data, map.Width, map.Height);
        }

        public static ArrayTextureMap<T> AsTextureMap<T>(this T[] array, int width)
            where T : struct
        {
            var height = array.Length / width;
            if (width * height != array.Length)
                throw new ArgumentException("The given width does not evenly divide the array.", nameof(width));
            return new ArrayTextureMap<T>(array, width, height);
        }


        public static void SaveAsPng(this ITextureMap<byte> map, string file)
        {
            var rgb = new Rgb24[map.Count];
            for (int i = 0; i < rgb.Length; i++)
            {
                var v = map[i];
                rgb[i] = new Rgb24(v, v, v);
            }

            using var image = Image.LoadPixelData(rgb, map.Width, map.Height);
            image.SaveAsPng(file);
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

            using var image = Image.LoadPixelData(rgb, map.Width, map.Height);
            image.SaveAsPng(file);
        }
        public static void SaveAsPng(this ITextureMap<Normal> map, string file)
        {
            var rgb = new Rgb24[map.Count];
            for (int i = 0; i < rgb.Length; i++)
            {
                var (r, g, b) = map[i].ToRGB();
                rgb[i] = new Rgb24(r, g, b);
            }

            using var image = Image.LoadPixelData(rgb, map.Width, map.Height);
            image.SaveAsPng(file);
        }
        public static void SaveAsPng(this ITextureMap<Rgba32> map, string file)
        {
            var rgb = new Rgba32[map.Count];
            for (int i = 0; i < rgb.Length; i++)
                rgb[i] = map[i];

            using var image = Image.LoadPixelData(rgb, map.Width, map.Height);
            image.SaveAsPng(file);
        }
        public static void SaveAsPng(this ITextureMap<Rgb24> map, string file)
        {
            var rgb = new Rgb24[map.Count];
            for (int i = 0; i < rgb.Length; i++)
                rgb[i] = map[i];

            using var image = Image.LoadPixelData(rgb, map.Width, map.Height);
            image.SaveAsPng(file);
        }
        public static void SaveAsPng(this ArrayTextureMap<Rgba32> map, string file)
        {
            using var image = Image.LoadPixelData(map.Data, map.Width, map.Height);
            image.SaveAsPng(file);
        }
        public static void SaveAsPng(this ArrayTextureMap<Rgb24> map, string file)
        {
            using var image = Image.LoadPixelData(map.Data, map.Width, map.Height);
            image.SaveAsPng(file);
        }

        public static void Transform(this ITextureMap<float> map, float offset = 0f, float scale = 0f)
        {
            var count = map.Count;
            for (int i = 0; i < count; i++)
                map[i] = map[i] * scale + offset;
        }
        public static void Normalize(this ITextureMap<float> map)
        {
            var count = map.Count;
            if (count < 1) return;

            var min = map[0];
            var max = map[0];
            for (int i = 1; i < count; i++)
            {
                var v = map[i];
                if (v < min) min = v;
                if (v > max) max = v;
            }

            var diff = max - min;
            if (diff < 0.001f)
            {
                map.Set(0f);
                return;
            }

            var f = 1f / diff;
            for (int i = 0; i < count; i++)
                map[i] = (map[i] - min) * f;
        }
        public static void Normalize(this ITextureMap<Normal> map)
        {
            var count = map.Count;
            for (int i = 0; i < count; i++)
                map[i] = map[i];
        }

        public static void CombineWith(this ITextureMap<Normal> map, ITextureMap<Normal> other, float strength)
        {
            map.CheckSameSize(other);

            var count = map.Count;
            for (int i = 0; i < count; i++)
                map[i] = Normal.HeightMapAddition(map[i], 1f, other[i], strength);
        }
    }

    public readonly struct Normal
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        private Normal(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }

        public static Normal FromVector(float x, float y, float z)
        {
            if (z < 0.0f)
            {
                x = -x;
                y = -y;
                z = -z;
            }

            var l = MathF.Sqrt(x * x + y * y + z * z);
            if (l < 0.001)
            {
                // The vector is too close to 0
                return new Normal(0.0f, 0.0f, 1.0f);
            }

            return new Normal(x / l, y / l, z / l);
        }
        public static Normal FromVector(Vector3 v) => FromVector(v.X, v.Y, v.Z);

        public static Normal FromXY(float x, float y)
        {
            var a = x * x + y * y;
            var zSq = 1.0f - a;
            if (zSq >= 0.0f)
            {
                return new Normal(x, y, MathF.Sqrt(zSq));
            }
            else
            {
                var f = 1.0f / MathF.Sqrt(a);
                return new Normal(x * f, y * f, 0.0f);
            }
        }
        public static Normal FromRG(byte r, byte g)
            => FromXY(r / 127.5f - 1.0f, g / 127.5f - 1.0f);

        public static Normal HeightMapAddition(Normal n, float nStrength, Normal m, float mStrength)
        {
            var nZ = n.Z < 0.001 ? 0.001f : n.Z;
            var mZ = m.Z < 0.001 ? 0.001f : m.Z;

            var nF = nStrength / -nZ;
            var mF = mStrength / -mZ;

            var a = new Vector3(1f, 0f, n.X * nF + m.X * mF);
            var b = new Vector3(0f, 1f, n.Y * nF + m.Y * mF);

            var r = Vector3.Cross(a, b);
            if (r.Z < 0f) r = -r;

            return FromVector(r);
        }

        public (byte r, byte g) ToRG() => (
            (byte)((Math.Clamp(X, -1.0f, 1.0f) + 1.0f) * 127.5f),
            (byte)((Math.Clamp(Y, -1.0f, 1.0f) + 1.0f) * 127.5f)
        );
        public (byte r, byte g, byte b) ToRGB()
        {
            var (r, g) = ToRG();
            return (r, g, (byte)(Math.Clamp(Z, 0.0f, 1.0f) * 255.0f));
        }

        public static implicit operator Vector3(Normal n) => new Vector3(n.X, n.Y, n.Z);
    }

    public readonly struct Slope
    {
        public readonly float dx;
        public readonly float dy;

        public Slope(float dx, float dy)
        {
            this.dx = dx;
            this.dy = dy;
        }

        public static Slope FromNormal(Normal n)
        {
            var f = -1f / (n.Z < 0.001f ? 0.001f : n.Z);
            return new Slope(n.X * f, n.Y * f);
        }

        public Normal ToNormal()
        {
            var a = new Vector3(1f, 0f, dx);
            var b = new Vector3(0f, 1f, dy);
            return Normal.FromVector(Vector3.Cross(a, b));
        }

        public static implicit operator Normal(Slope s) => s.ToNormal();
        public static explicit operator Slope(Normal n) => FromNormal(n);
    }
}
