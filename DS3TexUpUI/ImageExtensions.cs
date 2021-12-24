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

        public static ArrayTextureMap<P> DownSample<P, M, A>(this AverageAccumulatorFactory<P, A> factory, M map, int scale)
            where P : struct
            where M : ITextureMap<P>
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
    }
}
