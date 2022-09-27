using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Globalization;
using SoulsFormats;

namespace DS3TexUpUI
{
    public static class GX
    {
        public static GXIdType ParseGXId(this string id) => ParseGXId(id.AsSpan());
        public static GXIdType ParseGXId(this ReadOnlySpan<char> id)
        {
            if (id == "GXMD")
                return GXIdType.GXMD;
            if (id.StartsWith("GX") && id.Length == 4 && id[2].IsDigit() && id[3].IsDigit())
                return GXIdType.GX00;
            return GXIdType.Unknown;
        }

        public static bool IsGXMD(string id) => id.ParseGXId() == GXIdType.GXMD;
        public static bool IsGX00(string id) => id.ParseGXId() == GXIdType.GX00;

        public static List<GXValue> ToGxValues(this byte[] bytes) => ToGxValues(bytes.AsSpan());
        public static List<GXValue> ToGxValues(this ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length % 4 != 0)
                throw new ArgumentException("The number of bytes has to be divisible by 4.");

            var values = new List<GXValue>();
            for (int i = 0; i < bytes.Length; i += 4)
                values.Add(BitConverter.ToInt32(bytes.Slice(i, 4)));
            return values;
        }
        public static byte[] ToGxDataBytes(this IEnumerable<GXValue> values)
        {
            var bytes = new List<byte>();
            foreach (var value in values)
                bytes.AddRange(value.GetBytes());
            return bytes.ToArray();
        }

        /// <summary>
        /// A mapping from all known GX00 ids to its Unk04 value.
        /// </summary>
        public static IReadOnlyDictionary<string, int> GX00Unk04
            = Data.File("gx/gx00-unk04").LoadJsonFile<Dictionary<string, int>>();
        /// <summary>
        /// All observed Unk04 values for GXMD.
        /// </summary>
        public static IReadOnlyList<int> GXMDUnk04
            = Data.File("gx/gxmd-unk04").LoadJsonFile<List<int>>();
    }

    public enum GXIdType
    {
        GXMD,
        GX00,
        Unknown
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct GXValue
    {
        [FieldOffset(0)]
        public int I;
        [FieldOffset(0)]
        public float F;

        public GXValue(int i) : this()
        {
            I = i;
        }
        public GXValue(float f) : this()
        {
            F = f;
        }

        public byte[] GetBytes() => BitConverter.GetBytes(I);

        public override string ToString()
        {
            return I.ToString(CultureInfo.InvariantCulture) + "/" + F.ToString(CultureInfo.InvariantCulture);
        }

        public static implicit operator int(GXValue v) => v.I;
        public static implicit operator float(GXValue v) => v.F;
        public static implicit operator GXValue(int i) => new GXValue(i);
        public static implicit operator GXValue(float f) => new GXValue(f);
    }

    public enum GXValueType
    {
        Unknown = 0,
        Int = 1,
        Float = 2,
    }
}
