using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DS3TexUpUI
{
    public class DS3NormalMap
    {
        public readonly Rgba32[] Data;
        public readonly int Width;
        public readonly int Height;

        public NormalView Normals => new NormalView(this);
        public GlossView Gloss => new GlossView(this);
        public HeightView Heights => new HeightView(this);

        private DS3NormalMap(Rgba32[] data, int width, int height)
        {
            Data = data;
            Width = width;
            Height = height;
        }

        public static DS3NormalMap Load(string file)
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

            static Rgba32[] MapRows<T>(Image<T> image, Func<T, Rgba32> map) where T : unmanaged, IPixel<T>
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

            if (file.EndsWith(".dds"))
            {
                using var image = DDSImage.Load(file);
                var data = image.Format switch
                {
                    Pfim.ImageFormat.Rgb24 => FromBGR(image.Data),
                    Pfim.ImageFormat.Rgba32 => FromBGRA(image.Data),
                    _ => throw new Exception("Invalid format: " + image.Format)
                };

                return new DS3NormalMap(data, image.Width, image.Height);
            }
            else
            {
                using var image = Image.Load(file);
                var data = image switch
                {
                    Image<Bgr24> i => MapRows(i, p => new Rgba32(p.R, p.G, p.B, 255)),
                    Image<Bgra32> i => MapRows(i, p => new Rgba32(p.R, p.G, p.B, p.A)),
                    Image<Rgba32> i => MapRows(i, p => p),
                    Image<Rgb24> i => MapRows(i, p => new Rgba32(p.R, p.G, p.B, 255)),
                    _ => throw new Exception("Invalid format: " + image.PixelType.ToString())
                };
                return new DS3NormalMap(data, image.Width, image.Height);
            }
        }

        public void SaveAsPng(string file)
        {
            using var image = Image.LoadPixelData(Data, Width, Height);
            image.SaveAsPng(file);
        }

        public readonly struct NormalView : IReadOnlyList<Normal>
        {
            public readonly DS3NormalMap Map;

            public readonly Rgba32[] Data;

            public int Width => Map.Width;
            public int Height => Map.Height;
            public int Count => Width * Height;

            public NormalView(DS3NormalMap map)
            {
                Map = map;
                Data = map.Data;
            }

            public Normal this[int index]
            {
                get
                {
                    var p = Data[index];
                    return Normal.FromRG(p.R, p.G);
                }
                set
                {
                    var (r, g) = value.ToRG();
                    ref var p = ref Data[index];
                    p.R = r;
                    p.G = g;
                }
            }
            public Normal this[int x, int y]
            {
                get => this[y * Width + x];
                set => this[y * Width + x] = value;
            }

            public void Set(NormalView source) => Set(source.Map);
            public void Set(DS3NormalMap other)
            {
                var source = other.Data;

                for (int i = 0; i < Data.Length; i++)
                {
                    var s = source[i];
                    ref var t = ref Data[i];
                    t.R = s.R;
                    t.G = s.G;
                }
            }
            public void Set(Span<Normal> source)
            {
                for (int i = 0; i < Data.Length; i++)
                {
                    var (r, g) = source[i].ToRG();
                    ref var t = ref Data[i];
                    t.R = r;
                    t.G = g;
                }
            }

            public void CombineWith(NormalView other, float strength)
            {
                for (int i = 0; i < Count; i++)
                    this[i] = Normal.HeightMapAddition(n, 1f, m, strength);
            }

            public void SaveAsPng(string file)
            {
                var rgb = new Rgb24[Count];
                for (int i = 0; i < rgb.Length; i++)
                {
                    var (r, g, b) = this[i].ToRGB();
                    rgb[i] = new Rgb24(r, g, b);
                }

                using var image = Image.LoadPixelData(rgb, Width, Height);
                image.SaveAsPng(file);
            }

            public IEnumerator<Normal> GetEnumerator()
            {
                var count = Count;
                for (var i = 0; i < count; i++)
                    yield return this[i];
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public readonly struct GlossView : IReadOnlyList<byte>
        {
            public readonly DS3NormalMap Map;

            public readonly Rgba32[] Data;

            public int Width => Map.Width;
            public int Height => Map.Height;
            public int Count => Width * Height;

            public GlossView(DS3NormalMap map)
            {
                Map = map;
                Data = map.Data;
            }

            public byte this[int index]
            {
                get => Data[index].B;
                set => Data[index].B = value;
            }
            public byte this[int x, int y]
            {
                get => this[y * Width + x];
                set => this[y * Width + x] = value;
            }

            public void Set(GlossView source) => Set(source.Map);
            public void Set(DS3NormalMap other)
            {
                var source = other.Data;

                for (int i = 0; i < Data.Length; i++)
                    this[i] = source[i].B;
            }
            public void Set(Span<byte> source)
            {
                for (int i = 0; i < Data.Length; i++)
                    this[i] = source[i];
            }
            public void Set(byte source)
            {
                for (int i = 0; i < Data.Length; i++)
                    this[i] = source;
            }

            public void SaveAsPng(string file)
            {
                var rgb = new Rgb24[Count];
                for (int i = 0; i < rgb.Length; i++)
                {
                    var g = this[i];
                    rgb[i] = new Rgb24(g, g, g);
                }

                using var image = Image.LoadPixelData(rgb, Width, Height);
                image.SaveAsPng(file);
            }

            public IEnumerator<byte> GetEnumerator()
            {
                var count = Count;
                for (var i = 0; i < count; i++)
                    yield return this[i];
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public readonly struct HeightView : IReadOnlyList<byte>
        {
            public readonly DS3NormalMap Map;

            public readonly Rgba32[] Data;

            public int Width => Map.Width;
            public int Height => Map.Height;
            public int Count => Width * Height;

            public HeightView(DS3NormalMap map)
            {
                Map = map;
                Data = map.Data;
            }

            public byte this[int index]
            {
                get => Data[index].A;
                set => Data[index].A = value;
            }
            public byte this[int x, int y]
            {
                get => this[y * Width + x];
                set => this[y * Width + x] = value;
            }

            public bool IsPresent()
            {
                foreach (var p in Data)
                    if (p.A != 255)
                        return true;
                return false;
            }
            public bool IsNoticeable()
            {
                foreach (var p in Data)
                    if (p.A < 250)
                        return true;
                return false;
            }

            public void Set(HeightView source) => Set(source.Map);
            public void Set(DS3NormalMap other)
            {
                var source = other.Data;

                for (int i = 0; i < Data.Length; i++)
                    this[i] = source[i].A;
            }
            public void Set(Span<byte> source)
            {
                for (int i = 0; i < Data.Length; i++)
                    this[i] = source[i];
            }
            public void Set(byte source)
            {
                for (int i = 0; i < Data.Length; i++)
                    this[i] = source;
            }

            public void SaveAsPng(string file)
            {
                var rgb = new Rgb24[Count];
                for (int i = 0; i < rgb.Length; i++)
                {
                    var h = this[i];
                    rgb[i] = new Rgb24(h, h, h);
                }

                using var image = Image.LoadPixelData(rgb, Width, Height);
                image.SaveAsPng(file);
            }

            public IEnumerator<byte> GetEnumerator()
            {
                var count = Count;
                for (var i = 0; i < count; i++)
                    yield return this[i];
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
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
            if (z < 0.0f) z = 0;

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
