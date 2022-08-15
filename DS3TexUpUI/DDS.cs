using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Pfim;

#nullable enable

namespace DS3TexUpUI
{
    static class DDSExtensions
    {
        public static (DdsHeader, DdsHeaderDxt10?) ReadDdsHeader(this string file)
        {
            using var stream = File.OpenRead(file);
            var header = new DdsHeader(stream);
            if (header.PixelFormat.FourCC == CompressionAlgorithm.DX10)
            {
                var dxt10 = new DdsHeaderDxt10(stream);
                return (header, dxt10);
            }
            else
            {
                return (header, null);
            }
        }

        public static DDSFormat GetFormat(this (DdsHeader, DdsHeaderDxt10?) header)
            => GetFormat(header.Item1, header.Item2);
        public static DDSFormat GetFormat(this DdsHeader header, DdsHeaderDxt10? dxt10)
        {
            if (dxt10 == null)
                return header.PixelFormat.FourCC;
            else
                return dxt10.DxgiFormat;
        }

        public static void ToPNG(this string file, string target)
        {
            if (file.EndsWith(".dds"))
            {
                using var image = DDSImage.Load(file);
                image.SaveAsPng(target);
            }
            else
            {
                using var image = Image.Load(file);
                image.SaveAsPngWithDefaultEncoder(target);
            }
        }

        public static void ToDDS(this string file, string target, DDSFormat format, TexId id)
        {
            var uniform = id.GetTexKind() == TexKind.Normal;
            ToDDSUsingTexConv(file, target, format, uniformWeighting: uniform);
        }

        private static Random _tempRandom = new Random();
        public static void SaveAsDDS(this ITextureMap<Rgba32> image, string target, DDSFormat format, TexId id)
        {
            int n;
            lock (_tempRandom) { n = _tempRandom.Next(); }
            var temp = target + $"-temp{n}.png";
            image.SaveAsPng(temp);
            try
            {
                ToDDS(temp, target, format, id);
            }
            finally
            {
                File.Delete(temp);
            }
        }

        private static readonly Random _rng = new Random();
        internal static void ToDDSUsingTexConv(
            string file,
            string target,
            DDSFormat format,
            bool uniformWeighting = false,
            bool dithering = true,
            bool minimalCompression = false,
            bool maximumCompression = false
        )
        {
            var dir = Path.GetDirectoryName(target)!;
            var suffix = "-temp" + _rng.Next();

            // https://github.com/Microsoft/DirectXTex/wiki/Texconv
            var info = new ProcessStartInfo(AppConfig.Instance.TexConvExe);
            info.ArgumentList.Add("-nologo");
            info.ArgumentList.Add("-y"); // overwrite existing

            info.ArgumentList.Add("-f"); // output format
            info.ArgumentList.Add(ToTexConvFormat(format));
            if (uniformWeighting || dithering || minimalCompression || maximumCompression)
            {
                info.ArgumentList.Add("-bc");
                var f = "-";
                if (uniformWeighting) f += "u";
                if (dithering) f += "d";
                if (minimalCompression) f += "q";
                if (maximumCompression) f += "x";
                info.ArgumentList.Add(f);
            }
            info.ArgumentList.Add("-dx10");

            if (!format.IsSRGB) info.ArgumentList.Add("-srgbo");

            info.ArgumentList.Add("-sx");
            info.ArgumentList.Add(suffix);
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add(dir);
            info.ArgumentList.Add(file);
            var s = info.Arguments;

            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.RedirectStandardInput = true;
            info.CreateNoWindow = true;

            using var p = Process.Start(info)!;
            var output = p.StandardOutput.ReadToEnd();
            if (p.ExitCode != 0)
                throw new Exception("Unable to convert file: " + output);

            var tempFile = Path.Join(dir, Path.GetFileNameWithoutExtension(file) + suffix + ".dds");
            File.Move(tempFile, target, true);

            static string ToTexConvFormat(DDSFormat format)
            {
                if (format.DxgiFormat != DxgiFormat.UNKNOWN)
                    return format.DxgiFormat.ToString();
                throw new Exception("Invalid format: " + format);
            }
        }

        internal static void ToDDSUsingCompressonator(string file, string target)
        {
            var info = new ProcessStartInfo(AppConfig.Instance.CompressonatorCliExe);
            info.ArgumentList.Add("-fd");
            info.ArgumentList.Add("BC1");
            info.ArgumentList.Add("-DXT1UseAlpha");
            info.ArgumentList.Add("1");
            // info.ArgumentList.Add("-EncodeWith");
            // info.ArgumentList.Add("GPU");
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

            var p = Process.Start(info)!;
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new Exception("Unable to convert file: " + p.StandardOutput.ReadToEnd());
        }

        internal static void ToDDSUsingNVCompress(string file, string target, DDSFormat format)
        {
            var info = new ProcessStartInfo(AppConfig.Instance.NVCompressExe);
            info.ArgumentList.Add("-dds10");
            info.ArgumentList.Add("-silent");
            info.ArgumentList.Add("-alpha");
            info.ArgumentList.Add("-alpha_dithering");


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

            var p = Process.Start(info)!;
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
        public int BytesPerPixel
        {
            get
            {
                var bits = _image.BitsPerPixel;
                if (bits % 8 == 0) return bits / 8;
                throw new Exception();
            }
        }

        public Span<byte> Data => _image.Data.Slice(0, _image.DataLen);

        public Pfim.ImageFormat Format => _image.Format;

        private DDSImage(Pfim.IImage image)
        {
            _image = image;
        }

        public static DDSImage Load(string file)
        {
            var image = Pfim.Pfim.FromFile(file);
            if (image.Compressed) image.Decompress();

            return new DDSImage(image);
        }

        public void SaveAsPng(string file)
        {
            using var image = ToImage();
            image.SaveAsPngWithDefaultEncoder(file);
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
                    return GetMinMax(data, 0, BytesPerPixel).GetDiff() <= maxDiff;
                case Pfim.ImageFormat.Rgb24:
                    return GetMinMax(data, 0, BytesPerPixel).GetDiff() <= maxDiff
                        && GetMinMax(data, 1, BytesPerPixel).GetDiff() <= maxDiff
                        && GetMinMax(data, 2, BytesPerPixel).GetDiff() <= maxDiff;
                case Pfim.ImageFormat.Rgba32:
                    return GetMinMax(data, 0, BytesPerPixel).GetDiff() <= maxDiff
                        && GetMinMax(data, 1, BytesPerPixel).GetDiff() <= maxDiff
                        && GetMinMax(data, 2, BytesPerPixel).GetDiff() <= maxDiff
                        && GetMinMax(data, 3, BytesPerPixel).GetDiff() <= maxDiff;
                default:
                    throw new Exception("Unsupported pixel format (" + Format + ")");
            }
        }
        public RgbaDiff GetMaxAbsDiff()
        {
            var data = Data;
            if (data.Length == 0) return default;

            switch (Format)
            {
                case Pfim.ImageFormat.Rgb8:
                    return GetNonBorderMinMax(m => new RgbaMinMax(GetMinMax(m.Span, 0, 1)));
                case Pfim.ImageFormat.Rgb24:
                    return GetNonBorderMinMax(m => new RgbaMinMax(GetMinMax(m.Span, 2, 3), GetMinMax(m.Span, 1, 3), GetMinMax(m.Span, 0, 3)));
                case Pfim.ImageFormat.Rgba32:
                    return GetNonBorderMinMax(m => new RgbaMinMax(GetMinMax(m.Span, 2, 4), GetMinMax(m.Span, 1, 4), GetMinMax(m.Span, 0, 4), GetMinMax(m.Span, 3, 4)));
                default:
                    throw new Exception("Unsupported pixel format (" + Format + ")");
            }
        }
        private static MinMax GetMinMax(ReadOnlySpan<byte> data, int offset, int step)
        {
            var min = data[offset];
            var max = data[offset];
            for (int i = offset; i < data.Length; i += step)
            {
                var c = data[i];
                if (c < min) min = c;
                if (c > max) max = c;
            }
            return new MinMax(min, max);
        }
        private RgbaMinMax GetNonBorderMinMax(Func<ReadOnlyMemory<byte>, RgbaMinMax> func)
        {
            RgbaMinMax minmax = default;
            var first = true;
            foreach (var slice in ForAllNonBorder())
            {
                var mm = func(slice);
                if (first)
                    minmax = mm;
                else
                    minmax.UnionWith(mm);
                first = false;
            }
            return minmax;
        }
        private IEnumerable<ReadOnlyMemory<byte>> ForAllNonBorder()
        {
            var bpp = BytesPerPixel;
            var sliceLen = bpp * (Width - 2);
            var data = _image.Data.AsMemory(0, _image.DataLen);

            for (int y = 1; y < Height - 1; y++)
            {
                yield return data.Slice((y * Width + 1) * bpp, sliceLen);
            }
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

        ~DDSImage()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public enum TransparencyKind
    {
        None = 0,
        Unnoticeable = 1,
        Binary = 2,
        Full = 3,
    }

    public readonly struct DDSFormat : IEquatable<DDSFormat>
    {
        public CompressionAlgorithm FourCC { get; }
        public DxgiFormat DxgiFormat { get; }

        public bool IsSRGB
        {
            get
            {
                switch (ToDX10())
                {
                    case DxgiFormat.BC1_UNORM_SRGB:
                    case DxgiFormat.BC2_UNORM_SRGB:
                    case DxgiFormat.BC3_UNORM_SRGB:
                    case DxgiFormat.BC7_UNORM_SRGB:
                    case DxgiFormat.B8G8R8A8_UNORM_SRGB:
                    case DxgiFormat.B8G8R8X8_UNORM_SRGB:
                    case DxgiFormat.R8G8B8A8_UNORM_SRGB:
                        return true;
                    default:
                        return false;
                }
            }
        }

        public int? QualityScore
        {
            get
            {
                switch (FourCC)
                {
                    case CompressionAlgorithm.D3DFMT_DXT1:
                        return 1;
                    case CompressionAlgorithm.D3DFMT_DXT5:
                        return 3;
                    case CompressionAlgorithm.ATI1:
                        return 4;
                    case CompressionAlgorithm.ATI2:
                        return 5;
                    case CompressionAlgorithm.DX10:
                        switch (DxgiFormat)
                        {
                            case DxgiFormat.BC1_TYPELESS:
                            case DxgiFormat.BC1_UNORM_SRGB:
                            case DxgiFormat.BC1_UNORM:
                                return 1;
                            case DxgiFormat.BC3_UNORM_SRGB:
                                return 3;
                            case DxgiFormat.BC4_SNORM:
                            case DxgiFormat.BC4_TYPELESS:
                            case DxgiFormat.BC4_UNORM:
                                return 4;
                            case DxgiFormat.BC5_SNORM:
                            case DxgiFormat.BC5_TYPELESS:
                            case DxgiFormat.BC5_UNORM:
                                return 5;
                            case DxgiFormat.BC7_UNORM:
                            case DxgiFormat.BC7_UNORM_SRGB:
                                return 7;
                            case DxgiFormat.B8G8R8A8_UNORM_SRGB:
                            case DxgiFormat.B8G8R8X8_UNORM_SRGB:
                            case DxgiFormat.B5G5R5A1_UNORM:
                                // uncompressed
                                return 10;
                            default:
                                return null;
                        }

                    default:
                        return null;
                }
            }
        }

        public DDSFormat(CompressionAlgorithm fourCC)
        {
            FourCC = fourCC;
            DxgiFormat = DxgiFormat.UNKNOWN;
        }
        public DDSFormat(DxgiFormat dxgiFormat)
        {
            FourCC = CompressionAlgorithm.DX10;
            DxgiFormat = dxgiFormat;
        }

        public DxgiFormat ToDX10()
        {
            return FourCC switch
            {
                CompressionAlgorithm.ATI1 => DxgiFormat.BC4_UNORM,
                CompressionAlgorithm.ATI2 => DxgiFormat.BC5_UNORM,
                CompressionAlgorithm.D3DFMT_DXT1 => DxgiFormat.BC1_UNORM,
                CompressionAlgorithm.D3DFMT_DXT2 => this.DxgiFormat,
                CompressionAlgorithm.D3DFMT_DXT3 => DxgiFormat.BC2_UNORM,
                CompressionAlgorithm.D3DFMT_DXT4 => this.DxgiFormat,
                CompressionAlgorithm.D3DFMT_DXT5 => DxgiFormat.BC3_UNORM,
                CompressionAlgorithm.BC4S => DxgiFormat.BC4_SNORM,
                CompressionAlgorithm.BC5S => DxgiFormat.BC5_SNORM,
                CompressionAlgorithm.BC4U => DxgiFormat.BC4_UNORM,
                CompressionAlgorithm.BC5U => DxgiFormat.BC5_UNORM,
                _ => this.DxgiFormat,
            };
        }

        public override string ToString()
        {
            if (FourCC == CompressionAlgorithm.DX10) return $"DX10 {DxgiFormat}";
            return FourCC.ToString();
        }
        public static DDSFormat Parse(string input)
        {
            if (input.StartsWith("DX10 "))
                return Enum.Parse<DxgiFormat>(input.Substring(5));
            return Enum.Parse<CompressionAlgorithm>(input);
        }

        public override bool Equals(object? obj) => obj is DDSFormat other && Equals(other);
        public bool Equals(DDSFormat other)
        {
            return FourCC == other.FourCC &&
                   DxgiFormat == other.DxgiFormat;
        }

        public override int GetHashCode() => HashCode.Combine(FourCC, DxgiFormat);

        public static implicit operator DDSFormat(CompressionAlgorithm fourCC) => new DDSFormat(fourCC);
        public static implicit operator DDSFormat(DxgiFormat dxgiFormat) => new DDSFormat(dxgiFormat);

        public static bool operator ==(DDSFormat left, DDSFormat right) => left.Equals(right);
        public static bool operator !=(DDSFormat left, DDSFormat right) => left.Equals(right);
    }

    public readonly struct MinMax
    {
        public byte Min { get; }
        public byte Max { get; }

        public MinMax(byte value)
        {
            Min = value;
            Max = value;
        }
        public MinMax(byte min, byte max)
        {
            Min = min;
            Max = max;
        }

        public byte GetDiff() => (byte)(Max - Min);

        public MinMax Union(MinMax other)
        {
            return new MinMax(Math.Min(Min, other.Min), Math.Max(Max, other.Max));
        }
    }
    public struct RgbaMinMax
    {
        public MinMax R { get; set; }
        public MinMax G { get; set; }
        public MinMax B { get; set; }
        public MinMax A { get; set; }

        public RgbaMinMax(MinMax grey)
        {
            R = grey;
            G = grey;
            B = grey;
            A = new MinMax(255);
        }
        public RgbaMinMax(MinMax r, MinMax g, MinMax b)
        {
            R = r;
            G = g;
            B = b;
            A = new MinMax(255);
        }
        public RgbaMinMax(MinMax r, MinMax g, MinMax b, MinMax a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public RgbaDiff GetDiff() => new RgbaDiff(R.GetDiff(), G.GetDiff(), B.GetDiff(), A.GetDiff());
        public static implicit operator RgbaDiff(RgbaMinMax minmax) => minmax.GetDiff();

        public void UnionWith(RgbaMinMax other)
        {
            R = R.Union(other.R);
            G = G.Union(other.G);
            B = B.Union(other.B);
            A = A.Union(other.A);
        }
    }
    public struct RgbaDiff
    {
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public byte A { get; set; }

        public byte Max => Math.Max(Math.Max(R, G), Math.Max(B, A));
        public int Sum => R + G + B + A;

        public RgbaDiff(byte grey)
        {
            R = grey;
            G = grey;
            B = grey;
            A = 0;
        }
        public RgbaDiff(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
            A = 0;
        }
        public RgbaDiff(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public bool IsSolidColor(double tolerance = 0)
        {
            tolerance = Math.Clamp(tolerance, 0, 1);
            var maxDiff = (byte)(255 * tolerance);
            return Max <= maxDiff;
        }
    }
}
