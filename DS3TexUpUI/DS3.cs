using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;

namespace DS3TexUpUI
{
    public static class DS3
    {
        public static readonly string[] Maps = new string[]{
            "m30", // High Wall of Lothric, Consumed King's Garden, Lothric Castle
            "m31", // Undead Settlement
            "m32", // Archdragon Peak
            "m33", // Road of Sacrifices, Farron Keep
            "m34", // Grand Archives
            "m35", // Cathedral of the Deep
            "m37", // Irithyll of the Boreal Valley, Anor Londo
            "m38", // Catacombs of Carthus, Smouldering Lake
            "m39", // Irithyll Dungeon, Profaned Capital
            "m40", // Cemetary of Ash, Firelink Shrine, and Untended Graves
            "m41", // Kiln of the First Flame, Flameless Shrine
            "m45", // Painted World of Ariandel
            "m46", // Arena - Grand Roof
            "m47", // Arena - Kiln of Flame
            "m50", // Dreg Heap
            "m51", // The Ringed City, Filianore's Rest
            "m53", // Arena - Dragon Ruins
            "m54", // Arena - Round Plaza
        };

        /// <summary>
        /// A map from every (sensible) character file to the IDs of the characters it is used by.
        /// <para/>
        /// One file may be used by multiple characters.
        /// <para/>
        /// Useless and/or unused files are not in this map.
        /// </summary>
        public static IReadOnlyDictionary<string, ChrId[]> CharacterFiles = GetCharacterFiles();
        private static IReadOnlyDictionary<string, ChrId[]> GetCharacterFiles()
        {
            var file = Path.Join(AppDomain.CurrentDomain.BaseDirectory, @"data\chr.csv");
            var text = File.ReadAllText(file, Encoding.UTF8);

            var result = new Dictionary<string, ChrId[]>();

            foreach (var line in text.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                var parts = line.Split(';').Select(l => l.Trim()).ToArray();
                if (parts.Length < 2)
                    throw new FormatException("All files are expected to have at least one chr");

                var name = parts[0];
                var ids = new List<ChrId>();
                for (int i = 1; i < parts.Length; i++)
                    ids.Add(ChrId.Parse(parts[i]));

                if (result.ContainsKey(name))
                    throw new Exception("Duplicate file: " + name);

                result[name] = ids.ToArray();
            }

            return result;
        }

        public static IReadOnlyList<string> Parts = GetParts();
        private static IReadOnlyList<string> GetParts()
        {
            var file = Path.Join(AppDomain.CurrentDomain.BaseDirectory, @"data\parts.txt");
            var text = File.ReadAllText(file, Encoding.UTF8);

            return text.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        }

        public static class DDS
        {
            // Used for sRGB color textures without transparency
            public static readonly DDSFormat Albedo = DDSFormat.BC1_UNORM_SRGB;
            // Used for sRGB color textures with binary transparency.
            // (A pixel is either fully transparent or fully opaque.)
            public static readonly DDSFormat AlbedoBinaryAlpha = DDSFormat.BC1_UNORM_SRGB;
            // Used for sRGB color textures with transparency (RGBA).
            public static readonly DDSFormat AlbedoSmoothAlpha = DDSFormat.BC7_UNORM_SRGB;

            // They use BC7 for all normals I looked at.
            public static readonly DDSFormat Normal = DDSFormat.BC7_UNORM;

            public static readonly DDSFormat Reflective = DDSFormat.BC1_UNORM_SRGB;

            // They use BC4 for mask, but the header doesn't say whether signed or unsigned. Maybe it's typeless? idk
            // public static readonly DDSFormat Mask = ?;
        }
    }

    public readonly struct ChrId : IEquatable<ChrId>, IComparable<ChrId>
    {
        public readonly int IntValue;

        public ChrId(int intValue)
        {
            if (intValue < 0 || intValue > 9999) throw new ArgumentOutOfRangeException();
            IntValue = intValue;
        }

        public bool Equals(ChrId other) => IntValue == other.IntValue;
        public override bool Equals(object obj)
        {
            if (obj is ChrId other) return Equals(other);
            return false;
        }
        public override int GetHashCode() => IntValue.GetHashCode();
        public int CompareTo(ChrId other) => IntValue.CompareTo(other.IntValue);
        public override string ToString() => "c" + IntValue.ToString("D4");

        public static ChrId Parse(ReadOnlySpan<char> s)
        {
            if (s.StartsWith("c", StringComparison.OrdinalIgnoreCase)) s = s.Slice(1);
            if (s.Length != 4) throw new FormatException("Expected chr ID to contain exactly 4 digits");
            return new ChrId(int.Parse(s));
        }
    }
}
