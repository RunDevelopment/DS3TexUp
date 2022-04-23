using System;
using System.IO;
using System.Buffers.Binary;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace DS3TexUpUI
{
    public static class PngExtensions
    {
        public static void SaveAsPng(this ITextureMap<byte> map, string file)
        {
            var rgb = new Rgb24[map.Count];
            for (int i = 0; i < rgb.Length; i++)
            {
                var v = map[i];
                rgb[i] = new Rgb24(v, v, v);
            }
            rgb.AsTextureMap(map.Width).SaveAsPng(file);
        }
        public static void SaveAsPng(this ITextureMap<float> map, string file)
        {
            var rgb = new Rgb24[map.Count];
            for (int i = 0; i < rgb.Length; i++)
            {
                var f = Math.Clamp(map[i], 0f, 1f) * 255f;
                var v = (byte)f;
                rgb[i] = new Rgb24(v, v, v);
            }
            rgb.AsTextureMap(map.Width).SaveAsPng(file);
        }
        public static void SaveAsPng(this ITextureMap<Normal> map, string file)
        {
            var rgb = new Rgb24[map.Count];
            for (int i = 0; i < rgb.Length; i++)
            {
                var (r, g, b) = map[i].ToRGB();
                rgb[i] = new Rgb24(r, g, b);
            }
            rgb.AsTextureMap(map.Width).SaveAsPng(file);
        }
        public static void SaveAsPng(this ITextureMap<Rgba32> map, string file)
        {
            var rgb = new Rgba32[map.Count];
            for (int i = 0; i < rgb.Length; i++)
                rgb[i] = map[i];

            rgb.AsTextureMap(map.Width).SaveAsPng(file);
        }
        public static void SaveAsPng(this ITextureMap<Rgb24> map, string file)
        {
            var rgb = new Rgb24[map.Count];
            for (int i = 0; i < rgb.Length; i++)
                rgb[i] = map[i];

            rgb.AsTextureMap(map.Width).SaveAsPng(file);
        }

        public static void SaveAsPng(this ArrayTextureMap<Rgba32> map, string file)
        {
            using var image = Image.LoadPixelData(map.Data, map.Width, map.Height);
            image.SaveAsPngWithDefaultEncoder(file);
        }
        public static void SaveAsPng(this ArrayTextureMap<Rgb24> map, string file)
        {
            using var image = Image.LoadPixelData(map.Data, map.Width, map.Height);
            image.SaveAsPngWithDefaultEncoder(file);
        }

        public static void SaveAsPngWithDefaultEncoder(this Image image, string file)
        {
            var encoder = new PngEncoder();
            encoder.Gamma = 1 / 2.19995f;
            encoder.ChunkFilter = PngChunkFilter.None;
            encoder.CompressionLevel = PngCompressionLevel.BestSpeed;
            image.SaveAsPng(file, encoder);
        }

        public static Size ReadPngSize(string file)
        {
            foreach (var chunk in PngChunk.ReadHeaderChunks(file))
            {
                if (chunk.Type == PngChunkType.IHDR)
                {
                    var width = BinaryPrimitives.ReverseEndianness(BitConverter.ToInt32(chunk.Data.Span.Slice(0, 4)));
                    var height = BinaryPrimitives.ReverseEndianness(BitConverter.ToInt32(chunk.Data.Span.Slice(4, 4)));
                    return new Size(width, height);
                }
            }
            throw new Exception("Invalid PNG. Unable to find IHDR chunk.");
        }
    }

    struct PngChunk
    {
        public PngChunkType Type;
        public Memory<byte> Data;

        public PngChunk(PngChunkType type, Memory<byte> data)
        {
            Type = type;
            Data = data;
        }

        public static bool TryReadChunk(BinaryReader stream, out PngChunk chunk)
        {
            var (len, type) = ReadChunkHeader(stream);
            return TryReadChunkData(stream, len, type, out chunk);
        }
        public static (uint len, PngChunkType type) ReadChunkHeader(BinaryReader stream)
        {
            var len = BinaryPrimitives.ReverseEndianness(stream.ReadUInt32());
            var type = (PngChunkType)BinaryPrimitives.ReverseEndianness(stream.ReadUInt32());
            return (len, type);
        }
        public static bool TryReadChunkData(BinaryReader stream, uint len, PngChunkType type, out PngChunk chunk)
        {
            var data = new byte[len];
            if (len > 0)
            {
                if (stream.Read(data.AsSpan()) < len)
                {
                    // unexpected EOF
                    chunk = default;
                    return false;
                }
            }
            var crc = stream.ReadUInt32(); // ignore

            chunk = new PngChunk((PngChunkType)type, data);
            return true;
        }
        public static bool TryReadHeaderChunk(BinaryReader stream, out PngChunk chunk)
        {
            var (len, type) = ReadChunkHeader(stream);
            if (type == PngChunkType.IDAT || type == PngChunkType.IEND)
            {
                chunk = default;
                return false;
            }
            return TryReadChunkData(stream, len, type, out chunk);
        }

        public static IEnumerable<PngChunk> ReadHeaderChunks(string file)
        {
            using var br = new BinaryReader(File.OpenRead(file));
            br.ReadUInt64(); // first 8 bytes are unimportant
            while (TryReadHeaderChunk(br, out var chunk))
                yield return chunk;
        }
    }
    enum PngChunkType : uint
    {
        IHDR = 0x49484452u,
        IDAT = 0x49444154u,
        IEND = 0x49454e44u,
        gAMA = 0x67414d41u,
        pHYs = 0x70485973u,
        sRGB = 0x73524742u,
    }
}
