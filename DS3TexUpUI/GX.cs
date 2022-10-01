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
        public static IReadOnlyDictionary<(string id, int unk04), GX00ItemDescriptor> GX00Descriptor
            = Data.File("gx/gx00-descriptors.json").LoadJsonFile<List<GX00ItemDescriptor>>().ToDictionary(i => (i.ID, i.Unk04));
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
            if (I == 0) return "0";
            return I.ToString(CultureInfo.InvariantCulture) + "/" + F.ToString(CultureInfo.InvariantCulture);
        }
    }

    public class GX00ItemDescriptor
    {
        // E.g. "GX00", "GX80"
        public string ID { get; set; } = "Unknown";
        // The Unk04 value of this GX00.
        // All GX00 item lists seem to have a unique Unk04 value.
        public int Unk04 { get; set; }
        // If the category is not null, then all items are grouped together.
        // Note: Multiple item lists can have the same category.
        public string? Category { get; set; }
        public List<GX00ItemValueDescriptor> Items { get; set; } = new List<GX00ItemValueDescriptor>();
    }

    public class GX00ItemValueDescriptor
    {
        public string Name { get; set; } = "Unknown";
        public GX00ItemValueType Type { get; set; } = GX00ItemValueType.Unknown;
        // If the type is Int or Float, then this is the smallest accepted value.
        public float? Min { get; set; }
        // If the type is Int or Float, then this is the largest accepted value.
        public float? Max { get; set; }
        // If the type is Enum, then this lists all variants. This is a mapping from value to label.
        public Dictionary<int, string>? Enum { get; set; }
    }
    public enum GX00ItemValueType
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

    public enum GXMDItemType
    {
        /// <summary>
        /// The value is a <code>float</code>.
        /// <summary>
        Float = 1,
        /// <summary>
        /// The value is a <code>Vector2</code>.
        /// <summary>
        Float2 = 2,
        /// <summary>
        /// The value is a <code>Vector3</code>.
        /// <summary>
        Float3 = 3,
        /// <summary>
        /// The value is a <code>Float5</code>.
        /// <summary>
        Float5 = 11,
    }

    public struct Float5
    {
        public float Item0;
        public float Item1;
        public float Item2;
        public float Item3;
        public float Item4;

        public Float5(float all)
        {
            Item0 = all;
            Item1 = all;
            Item2 = all;
            Item3 = all;
            Item4 = all;
        }
        public Float5(float item0, float item1, float item2, float item3, float item4)
        {
            Item0 = item0;
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
        }
    }

    public class GXMDItem
    {
        public GXMDItemType Type { get; set; }
        public object Value { get; set; }
        public bool? Flagged { get; set; } = null;

        public GXMDItem(GXMDItemType type, object value)
        {
            Type = type;
            Value = value;
        }
        public GXMDItem() : this(GXMDItemType.Float, 0f) { }
        public GXMDItem(float value) : this(GXMDItemType.Float, value) { }
        public GXMDItem(Vector2 value) : this(GXMDItemType.Float2, value) { }
        public GXMDItem(Vector3 value) : this(GXMDItemType.Float3, value) { }
        public GXMDItem(Float5 value) : this(GXMDItemType.Float5, value) { }

        public void Validate()
        {
            void AssertType<T>()
            {
                if (Value is null)
                    throw new FormatException($"Expected {Type} item to have a {typeof(T)} value but found null.");
                if (!(Value is T))
                    throw new FormatException($"Expected {Type} item to have a {typeof(T)} value but found {Value.GetType()}.");
            }

            switch (Type)
            {
                case GXMDItemType.Float:
                    AssertType<float>();
                    break;
                case GXMDItemType.Float2:
                    AssertType<Vector2>();
                    break;
                case GXMDItemType.Float3:
                    AssertType<Vector3>();
                    break;
                case GXMDItemType.Float5:
                    AssertType<Float5>();
                    break;
                default:
                    throw new FormatException($"{Type} is not a valid type.");
            }
        }
    }

    public class GXMD
    {
        public Dictionary<int, GXMDItem> Items { get; set; }

        public GXMD(Dictionary<int, GXMDItem> items)
        {
            Items = items;
        }
        public GXMD() : this(new Dictionary<int, GXMDItem>()) { }

        private struct GXValueReader
        {
            private int index;
            private GXValue[] values;

            public bool End => index == values.Length;

            public GXValueReader(byte[] data)
            {
                index = 0;
                values = data.ToGxValues();
            }

            private GXValue Read()
            {
                if (index >= values.Length)
                {
                    throw new FormatException("Stream of GXValues ended early.");
                }
                return values[index++];
            }
            public int ReadInt() => Read().I;
            public float ReadFloat() => Read().F;
        }

        public static GXMD FromBytes(byte[] data)
        {
            var reader = new GXValueReader(data);

            var result = new Dictionary<int, GXMDItem>();

            var count = reader.ReadInt();
            for (var j = 0; j < count; j++)
            {
                var id = reader.ReadInt();
                var dataType = reader.ReadInt();

                if (dataType == 0)
                {
                    var value = reader.ReadInt();
                    if (value != 0 && value != 1)
                        throw new FormatException($"Invalid flag value {value}. Expected 0 or 1.");

                    if (result.TryGetValue(id, out var item))
                    {
                        if (item.Flagged != null)
                            throw new FormatException($"{id} cannot be flagged twice.");
                        item.Flagged = value != 0;
                    }
                    else
                    {
                        throw new FormatException($"{id} cannot be flagged because it doesn't exist.");
                    }
                }
                else
                {
                    GXMDItem item;
                    switch ((GXMDItemType)dataType)
                    {
                        case GXMDItemType.Float:
                            item = new GXMDItem(reader.ReadFloat());
                            break;
                        case GXMDItemType.Float2:
                            item = new GXMDItem(new Vector2(reader.ReadFloat(), reader.ReadFloat()));
                            break;
                        case GXMDItemType.Float3:
                            item = new GXMDItem(new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()));
                            break;
                        case GXMDItemType.Float5:
                            item = new GXMDItem(new Float5(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()));
                            break;
                        default:
                            throw new FormatException($"Unknown data type {dataType}");
                    }

                    result.Add(id, item);
                }
            }

            if (!reader.End)
                throw new FormatException($"Invalid bytes. There are still values left after parsing the GXMD.");

            return new GXMD(result);
        }

        private List<GXValue> ToValues()
        {
            var result = new List<GXValue>();
            void WriteInt(int i) => result.Add(new GXValue(i: i));
            void WriteFloat(float f) => result.Add(new GXValue(f: f));

            WriteInt(Items.Count + Items.Values.Where(i => i.Flagged.HasValue).Count());

            foreach (var (id, item) in Items)
            {
                WriteInt(id);
                WriteInt((int)item.Type);

                item.Validate();

                switch (item.Type)
                {
                    case GXMDItemType.Float:
                        {
                            var value = (float)item.Value;
                            WriteFloat(value);
                            break;
                        }
                    case GXMDItemType.Float2:
                        {
                            var value = (Vector2)item.Value;
                            WriteFloat(value.X);
                            WriteFloat(value.Y);
                            break;
                        }
                    case GXMDItemType.Float3:
                        {
                            var value = (Vector3)item.Value;
                            WriteFloat(value.X);
                            WriteFloat(value.Y);
                            WriteFloat(value.Z);
                            break;
                        }
                    case GXMDItemType.Float5:
                        {
                            var value = (Float5)item.Value;
                            WriteFloat(value.Item0);
                            WriteFloat(value.Item1);
                            WriteFloat(value.Item2);
                            WriteFloat(value.Item3);
                            WriteFloat(value.Item4);
                            break;
                        }
                    default:
                        throw new FormatException($"Invalid item type {item.Type}");
                }

                if (item.Flagged.HasValue)
                {
                    WriteInt(id);
                    WriteInt(0);
                    WriteInt(item.Flagged.Value ? 1 : 0);
                }
            }

            return result;
        }
        public byte[] ToBytes() => ToValues().ToGxDataBytes();
    }
}
