using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SoulsFormats;

namespace DS3TexUpUI
{
    public class GXItemBuilder
    {
        public string ID;
        public int Unk04;
        public List<GXValue> Values;

        public GXItemBuilder(string id, int unk04)
        {
            ID = id;
            Unk04 = unk04;
            Values = new List<GXValue>();
        }
        public GXItemBuilder(FLVER2.GXItem item)
        {
            ID = item.ID;
            Unk04 = item.Unk04;
            Values = BytesToValues(item.Data);
        }

        public FLVER2.GXItem ToItem()
        {
            return new FLVER2.GXItem(ID, Unk04, ValuesToBytes(Values));
        }

        private static List<GXValue> BytesToValues(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length % 4 != 0)
                throw new ArgumentException("The number of bytes has to be divisible by 4.");

            var values = new List<GXValue>();
            for (int i = 0; i < bytes.Length; i += 4)
                values.Add(BitConverter.ToInt32(bytes.Slice(i, 4)));
            return values;
        }
        private static byte[] ValuesToBytes(List<GXValue> values)
        {
            var bytes = new List<byte>();
            foreach (var value in values)
                bytes.AddRange(value.GetBytes());
            return bytes.ToArray();
        }
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

        public static implicit operator int(GXValue v) => v.I;
        public static implicit operator float(GXValue v) => v.F;
        public static implicit operator GXValue(int i) => new GXValue(i);
        public static implicit operator GXValue(float f) => new GXValue(f);
    }
}
