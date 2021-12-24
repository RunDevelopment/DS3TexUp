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
            if (file.EndsWith(".dds"))
            {
                using var image = DDSImage.Load(file);
                return Of(image);
            }
            else
            {
                using var image = Image.Load(file);
                return Of(image);
            }
        }

        public static DS3NormalMap Of(DDSImage image)
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

            return new DS3NormalMap(data, image.Width, image.Height);
        }
        public static DS3NormalMap Of(Image image)
        {
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

        public void SaveAsPng(string file)
        {
            using var image = Image.LoadPixelData(Data, Width, Height);
            image.SaveAsPng(file);
        }

        public readonly struct NormalView : ITextureMap<Normal>
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

            public IEnumerator<Normal> GetEnumerator()
            {
                var count = Count;
                for (var i = 0; i < count; i++)
                    yield return this[i];
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public readonly struct GlossView : ITextureMap<byte>
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

            public IEnumerator<byte> GetEnumerator()
            {
                var count = Count;
                for (var i = 0; i < count; i++)
                    yield return this[i];
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public readonly struct HeightView : ITextureMap<byte>
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

            public IEnumerator<byte> GetEnumerator()
            {
                var count = Count;
                for (var i = 0; i < count; i++)
                    yield return this[i];
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
