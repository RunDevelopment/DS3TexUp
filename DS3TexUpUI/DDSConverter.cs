﻿using System;
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
        public static void ToPNG(string file, string target)
        {
            if (file.EndsWith(".dds"))
            {
                using var image = DDSImage.Load(file);
                image.SaveAsPng(target);
            }
            else
            {
                using var image = Image.Load(file);
                image.SaveAsPng(target);
            }
        }

        public static void ToDDS(string file, string target, DDSFormat format)
        {
            ToDDSUsingTexConv(file, target, format);
        }

        private static readonly string _texConvPath = @"C:\DS3TexUp\texconv.exe";
        private static readonly Random _rng = new Random();
        private static void ToDDSUsingTexConv(string file, string target, DDSFormat format)
        {
            var dir = Path.GetDirectoryName(target);
            var suffix = "-temp" + _rng.Next();

            // https://github.com/Microsoft/DirectXTex/wiki/Texconv
            var info = new ProcessStartInfo(_texConvPath);
            info.ArgumentList.Add("-nologo");
            info.ArgumentList.Add("-y"); // overwrite existing

            info.ArgumentList.Add("-f"); // output format
            info.ArgumentList.Add(ToTexConvFormat(format));
            info.ArgumentList.Add("-bc");
            info.ArgumentList.Add("-d");
            info.ArgumentList.Add("-dx10");

            info.ArgumentList.Add("-sx");
            info.ArgumentList.Add(suffix);
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add(dir);
            info.ArgumentList.Add(file);

            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.RedirectStandardInput = true;
            info.CreateNoWindow = true;

            var p = Process.Start(info);
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new Exception("Unable to convert file: " + p.StandardError.ReadToEnd());

            var tempFile = Path.Join(dir, Path.GetFileNameWithoutExtension(file) + suffix + ".dds");
            File.Move(tempFile, target, true);
        }
        private static string ToTexConvFormat(DDSFormat format)
        {
            return format switch
            {
                DDSFormat.BC1_UNORM => "BC1_UNORM",
                DDSFormat.BC1_UNORM_SRGB => "BC1_UNORM_SRGB",
                DDSFormat.BC7_UNORM => "BC7_UNORM",
                DDSFormat.BC7_UNORM_SRGB => "BC7_UNORM_SRGB",
                _ => throw new Exception("Invalid format: " + format),
            };
        }


        private static readonly string _compressonatorCli = @"C:\Program Files (x86)\Compressonator_4.2.5185\bin\CLI\compressonatorcli.exe";
        private static void ToDDSUsingCompressonator(string file, string target)
        {
            var info = new ProcessStartInfo(_compressonatorCli);
            info.ArgumentList.Add("-fd");
            info.ArgumentList.Add("BC1");
            info.ArgumentList.Add("-DXT1UseAlpha");
            info.ArgumentList.Add("1");
            info.ArgumentList.Add("-EncodeWith");
            info.ArgumentList.Add("GPU");
            info.ArgumentList.Add("-RefineSteps");
            info.ArgumentList.Add("2");
            info.ArgumentList.Add("-miplevels");
            info.ArgumentList.Add("20");

            info.ArgumentList.Add(file);
            info.ArgumentList.Add(target);

            info.CreateNoWindow = true;
            info.RedirectStandardError = true;
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true;

            var p = Process.Start(info);
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new Exception("Unable to convert file: " + p.StandardOutput.ReadToEnd());
        }
    }

    public class DDSImage : IDisposable
    {
        internal readonly Pfim.IImage _image;
        private bool disposedValue;

        public int Width => _image.Width;
        public int Height => _image.Height;

        public Span<byte> Data => _image.Data.Slice(0, _image.DataLen);

        public Pfim.ImageFormat Format => _image.Format;

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

        public void SaveAsPng(string file)
        {
            using var image = ToImage();
            image.SaveAsPng(file);
        }

        public TransparencyKind GetTransparency()
        {
            var data = Data;

            switch (Format)
            {
                case Pfim.ImageFormat.Rgb8:
                case Pfim.ImageFormat.R5g5b5:
                case Pfim.ImageFormat.R5g6b5:
                case Pfim.ImageFormat.Rgb24:
                    return TransparencyKind.None;

                case Pfim.ImageFormat.Rgba32:
                    {
                        var map = new bool[256];
                        for (var i = 3; i < data.Length; i += 4)
                        {
                            map[data[i]] = true;
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

                case Pfim.ImageFormat.R5g5b5a1:
                    return TransparencyKind.Binary;

                default:
                    throw new Exception("Unsupported pixel format (" + Format + ")");
            }
        }

        public bool IsSolidColor(double tolerance = 0)
        {
            tolerance = Math.Clamp(tolerance, 0, 1);
            var maxDiff = (byte)(255 * tolerance);

            var data = Data;
            if (data.Length == 0) return true;

            switch (Format)
            {
                case Pfim.ImageFormat.Rgb8:
                    return GetAbsDiff(data, 0, 1) <= maxDiff;
                case Pfim.ImageFormat.Rgb24:
                    return GetAbsDiff(data, 0, 3) <= maxDiff
                        && GetAbsDiff(data, 1, 3) <= maxDiff
                        && GetAbsDiff(data, 2, 3) <= maxDiff;
                case Pfim.ImageFormat.Rgba32:
                    return GetAbsDiff(data, 0, 4) <= maxDiff
                        && GetAbsDiff(data, 1, 4) <= maxDiff
                        && GetAbsDiff(data, 2, 4) <= maxDiff
                        && GetAbsDiff(data, 3, 4) <= maxDiff;
                default:
                    throw new Exception("Unsupported pixel format (" + Format + ")");
            }
        }
        private static byte GetAbsDiff(Span<byte> data, int offset, int step)
        {
            var min = data[offset];
            var max = data[offset];
            for (int i = offset; i < data.Length; i += step)
            {
                var c = data[i];
                if (c < min) min = c;
                if (c > max) max = c;
            }
            return (byte)(max - min);
        }

        public Image ToImage()
        {
            var data = _image.Data;

            switch (Format)
            {
                case Pfim.ImageFormat.Rgb8:
                    return Image.LoadPixelData<Bgr24>(Data.Duplicate(3), _image.Width, _image.Height);
                case Pfim.ImageFormat.Rgb24:
                    return Image.LoadPixelData<Bgr24>(data, _image.Width, _image.Height);
                case Pfim.ImageFormat.Rgba32:
                    return Image.LoadPixelData<Bgra32>(data, _image.Width, _image.Height);
                default:
                    throw new Exception("Unsupported pixel format (" + Format + ")");
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

    public enum TransparencyKind
    {
        None,
        Unnoticeable,
        Binary,
        Full,
    }

    public enum DDSFormat
    {
        BC1_UNORM = 71,
        BC1_UNORM_SRGB = 72,
        BC7_UNORM = 98,
        BC7_UNORM_SRGB = 99,
    }
}
