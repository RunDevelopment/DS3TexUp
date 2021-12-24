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

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var item in Data)
                yield return item;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class MapExtensions
    {
        public static void Set<T, M, N>(this M map, N source)
            where T : struct
            where M : ITextureMap<T>
            where N : ITextureMap<T>
        {
            var count = map.Count;
            for (int i = 0; i < count; i++)
                map[i] = source[i];
        }
        public static void Set<T, M>(this M map, Span<T> source)
            where T : struct
            where M : ITextureMap<T>
        {
            var count = map.Count;
            for (int i = 0; i < count; i++)
                map[i] = source[i];
        }
        public static void Set<T, M>(this M map, T source)
            where T : struct
            where M : ITextureMap<T>
        {
            var count = map.Count;
            for (int i = 0; i < count; i++)
                map[i] = source;
        }

        public static ArrayTextureMap<O> Convert<T, O, M>(this M map, Func<T, O> converter)
            where T : struct
            where O : struct
            where M : ITextureMap<T>
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
    }

    public static class ByteMapExtensions
    {
        public static void SaveAsPng<M>(this M map, string file)
             where M : ITextureMap<byte>
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
    }

    public static class FloatMapExtensions
    {
        public static void SaveAsPng<M>(this M map, string file)
             where M : ITextureMap<float>
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

        public static void Transform<M>(this M map, float offset = 0f, float scale = 0f)
             where M : ITextureMap<float>
        {
            var count = map.Count;
            for (int i = 0; i < count; i++)
                map[i] = map[i] * scale + offset;
        }
        public static void Normalize<M>(this M map)
             where M : ITextureMap<float>
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
    }

    public static class NormalMapExtensions
    {
        public static void SaveAsPng<M>(this M map, string file)
            where M : ITextureMap<Normal>
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

        public static void CombineWith<M, N>(this M map, N other, float strength)
            where M : ITextureMap<Normal>
            where N : ITextureMap<Normal>
        {
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
}
