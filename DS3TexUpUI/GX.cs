using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using SoulsFormats;

#nullable enable

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

        public static GXValue[] ToGxValues(this byte[] bytes) => ToGxValues(bytes.AsSpan());
        public static GXValue[] ToGxValues(this ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length % 4 != 0)
                throw new ArgumentException("The number of bytes has to be divisible by 4.");

            var values = new GXValue[bytes.Length / 4];
            for (int i = 0; i < values.Length; i++)
                values[i] = new GXValue(i: BitConverter.ToInt32(bytes.Slice(i * 4, 4)));
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
        /// All observed Unk04 values for GXMD.
        /// </summary>
        public static IReadOnlyList<int> GXMDUnk04
            = Data.File("gx/gxmd-unk04.json").LoadJsonFile<List<int>>();
        public static IReadOnlyDictionary<string, GX00ItemListDescriptor> GX00Descriptor
            = Data.File("gx/gx00-descriptors.json").LoadJsonFile<List<GX00ItemListDescriptor>>().ToDictionary(i => i.ID);
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
    }

    public class GX00ItemListDescriptor
    {
        // E.g. "GX00", "GX80"
        public string ID { get; set; } = "Unknown";
        // The Unk04 value of this GX00.
        // All GX00 item lists seem to have a unique Unk04 value.
        public int Unk04 { get; set; }
        // If the category is not null, then all items are grouped together.
        // Note: Multiple item lists can have the same category.
        public string? Category { get; set; }
        public List<GX00ItemDescriptor> Items { get; set; } = new List<GX00ItemDescriptor>();
    }

    public class GX00ItemDescriptor
    {
        public string Name { get; set; } = "Unknown";
        public GX00ItemType Type { get; set; } = GX00ItemType.Unknown;
        // If the type is Int or Float, then this is the smallest accepted value.
        public float? Min { get; set; }
        // If the type is Int or Float, then this is the largest accepted value.
        public float? Max { get; set; }
        // If the type is Enum, then this lists all variants. This is a mapping from value to label.
        public Dictionary<int, string>? Enum { get; set; }
    }
    public enum GX00ItemType
    {
        // The type is unknown. This might be because the item is unused.
        Unknown = 0,
        // The type is int32.
        Int = 1,
        // The type is float.
        Float = 2,
        // The type is int32, but only certain values are allowed.
        // See GX00ItemDescriptor#Enum for all accepted values.
        Enum = 3,
        // The type is int32, but only 0 (false) and 1 (true) are accepted.
        Bool = 4,
    }
    }
}
