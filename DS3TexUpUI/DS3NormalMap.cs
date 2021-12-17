using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
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

        public byte[] GetGlossData() => Data.AsSpan().Map(p => p.B);
        public byte[] GetHeightData() => Data.AsSpan().Map(p => p.A);


        public void SaveGlossAsPng(string file)
        {
            using var image = Image.LoadPixelData<Bgr24>(GetGlossData().AsSpan().Duplicate(3), Width, Height);
            image.SaveAsPng(file);
        }
        public void SaveHeightAsPng(string file)
        {
            using var image = Image.LoadPixelData<Bgr24>(GetHeightData().AsSpan().Duplicate(3), Width, Height);
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
                var target = Map.Data;

                for (int i = 0; i < source.Length; i++)
                {
                    var s = source[i];
                    ref var t = ref target[i];
                    t.R = s.R;
                    t.G = s.G;
                }
            }
            public void Set(Span<Normal> source)
            {
                var target = Map.Data;

                for (int i = 0; i < source.Length; i++)
                {
                    var (r, g) = source[i].ToRG();
                    ref var t = ref target[i];
                    t.R = r;
                    t.G = g;
                }
            }

            public void CombineWith(NormalView other, float strength)
            {
                for (int i = 0; i < Count; i++)
                {
                    var n = this[i];
                    var m = other[i];

                    if (n.Z < 0.001) n = Normal.FromVector(n.X, n.Y, 0.001f);
                    if (m.Z < 0.001) m = Normal.FromVector(m.X, m.Y, 0.001f);

                    var nF = 1.0f / -n.Z;
                    var mF = strength / -m.Z;

                    var a = new Vector3(1.0f, 1.0f, (n.X + n.Y) * nF + (m.X + m.Y) * mF);
                    var b = new Vector3(1.0f, -1.0f, (n.X - n.Y) * nF + (m.X - m.Y) * mF);

                    var r = Vector3.Cross(a, b);
                    if (r.Z < 0.0f) r = -r;

                    this[i] = Normal.FromVector(r);
                }
            }

            public void EnhanceWith(NormalView other, float strength)
            {
                for (int i = 0; i < Count; i++)
                {
                    var a = this[i];
                    var b = other.Data[i];

                    var bX = b.R / 127.5f - 1.0f;
                    var bY = b.G / 127.5f - 1.0f;

                    this[i] = Normal.FromVector(a.X + bX * strength, a.Y + bY * strength, a.Z);
                }
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
