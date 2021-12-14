using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DS3TexUpUI
{
    class DDSConverter
    {
        public static void ToPNG(string file, string targetDir)
        {
            var target = Path.Join(targetDir, Path.GetFileNameWithoutExtension(file) + ".png");

            if (file.EndsWith(".dds"))
            {
                using (var image = DDSImage.Load(file))
                {
                    Save(image.ToImage(), target);
                }
            }
            else
            {
                Save(Image.Load(file), target);
            }
        }

        private static void Save(Image image, string target)
        {
            using (image)
            {
                image.SaveAsPng(target);
            }
        }

        private static readonly string _texConvPath = @"C:\DS3TexUp\texconv.exe";

        public static void ToDDS(string file, string targetDir, DDSFormat format = DDSFormat.BC1_UNORM, bool sRGB = true)
        {
            // https://github.com/Microsoft/DirectXTex/wiki/Texconv
            var info = new ProcessStartInfo();
            info.FileName = _texConvPath;

            info.ArgumentList.Add("-nologo");
            info.ArgumentList.Add("-y"); // overwrite existing
            info.ArgumentList.Add("-f"); // output format
            info.ArgumentList.Add(ToTexConvFormat(format));
            info.ArgumentList.Add("-dx10");
            info.ArgumentList.Add("-bc");
            info.ArgumentList.Add("-d");
            info.ArgumentList.Add(sRGB ? "-sRGB" : "-sRGBi");
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add(targetDir);
            info.ArgumentList.Add(file);

            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.RedirectStandardInput = true;
            info.CreateNoWindow = true;

            var process = new Process();
            process.StartInfo = info;
            process.Start();

            var error = process.StandardError.ReadToEnd();
            if (error.Length > 0)
            {
                Console.WriteLine(error);
            }
        }
        private static string ToTexConvFormat(DDSFormat format)
        {
            switch (format)
            {
                case DDSFormat.BC1_UNORM:
                    return "BC1_UNORM";
                case DDSFormat.BC3_UNORM:
                    return "BC3_UNORM";
                default:
                    throw new Exception("Invalid DDSForamt");
            }
        }
    }

    public class DDSImage : IDisposable
    {
        private readonly Pfim.IImage _image;
        private bool disposedValue;

        private DDSImage(Pfim.IImage image)
        {
            _image = image;
        }

        public static DDSImage Load(string file)
        {
            var image = Pfim.Pfim.FromFile(file);
            if (image == null) throw new Exception("Unable to decode file: " + file);

            if (image.Compressed) image.Decompress();

            return new DDSImage(image);
        }

        public bool HasTransparency()
        {
            var data = _image.Data;

            switch (_image.Format)
            {
                case Pfim.ImageFormat.Rgb8:
                case Pfim.ImageFormat.R5g5b5:
                case Pfim.ImageFormat.R5g6b5:
                case Pfim.ImageFormat.Rgb24:
                    return false;

                case Pfim.ImageFormat.Rgba32:
                    for (var i = 3; i < data.Length; i += 4)
                    {
                        // some images have rnadom pixels with 254 as their alpha value
                        if (data[i] < 250)
                            return true;
                    }
                    return false;

                default:
                    throw new Exception("Unsupported pixel format (" + _image.Format + ")");
            }
        }

        public Image ToImage()
        {
            var data = _image.Data;

            switch (_image.Format)
            {
                case Pfim.ImageFormat.Rgb8:
                    {
                        var bytes = new byte[data.Length * 3];
                        for (var i = 0; i < data.Length; i++)
                        {
                            var v = data[i];
                            bytes[i * 3 + 0] = v;
                            bytes[i * 3 + 1] = v;
                            bytes[i * 3 + 2] = v;
                        }
                        return Image.LoadPixelData<Bgr24>(bytes, _image.Width, _image.Height);
                    }
                case Pfim.ImageFormat.Rgb24:
                    return Image.LoadPixelData<Bgr24>(data, _image.Width, _image.Height);
                case Pfim.ImageFormat.Rgba32:
                    return Image.LoadPixelData<Bgra32>(data, _image.Width, _image.Height);
                default:
                    throw new Exception("Unsupported pixel format (" + _image.Format + ")");
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                _image.Dispose();

                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~DSSImage()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    enum DDSFormat
    {
        BC1_UNORM,
        BC3_UNORM
    }
}
