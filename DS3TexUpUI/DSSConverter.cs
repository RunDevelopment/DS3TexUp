using System;
using System.Collections.Generic;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DS3TexUpUI
{
    class DSSConverter
    {
        public void ToPNG(string source, string target)
        {
            if (source.EndsWith(".dds"))
            {
                var image = Pfim.Pfim.FromFile(source);
                if (image == null) throw new Exception("Unable to decode file: " + source);

                if (image.Compressed) image.Decompress();

                if (image.Format == Pfim.ImageFormat.Rgba32)
                    Save(Image.LoadPixelData<Bgra32>(image.Data, image.Width, image.Height), target);
                else if (image.Format == Pfim.ImageFormat.Rgb24)
                    Save(Image.LoadPixelData<Bgr24>(image.Data, image.Width, image.Height), target);
                else
                    throw new Exception("Unsupported pixel format (" + image.Format + ")");
            }
            else
            {
                Image.Load(source).SaveAsPng(target);
            }
        }

        public void ToDDS(string file)
        {

        }

        private void Save<T>(Image<T> image, string target) where T : unmanaged, IPixel<T>
        {
            image.SaveAsPng(target);
        }
    }
}
