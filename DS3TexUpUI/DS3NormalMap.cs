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

        private DS3NormalMap(ArrayTextureMap<Rgba32> map) : this(map.Data, map.Width, map.Height) { }
        private DS3NormalMap(Rgba32[] data, int width, int height)
        {
            Data = data;
            Width = width;
            Height = height;
        }

        public static DS3NormalMap Load(string file) => new DS3NormalMap(file.LoadTextureMap());
        public static DS3NormalMap Of(DDSImage image) => new DS3NormalMap(image.ToTextureMap());
        public static DS3NormalMap Of(Image image) => new DS3NormalMap(image.ToTextureMap());
        public static DS3NormalMap Of(ArrayTextureMap<Rgba32> image) => new DS3NormalMap(image);

        public void SaveAsPng(string file)
        {
            using var image = Image.LoadPixelData(Data, Width, Height);
            image.SaveAsPngWithDefaultEncoder(file);
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
