using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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
}
