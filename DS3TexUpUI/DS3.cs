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

        public static IReadOnlyList<string> Parts = GetParts();
        private static IReadOnlyList<string> GetParts()
        {
            var file = Path.Join(AppDomain.CurrentDomain.BaseDirectory, @"data\parts.txt");
            var text = File.ReadAllText(file, Encoding.UTF8);

            return text.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        }

        public static TransparencyIndex Transparency = TransparencyIndex.Load(Path.Join(AppDomain.CurrentDomain.BaseDirectory, @"data\alpha.txt"));

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
}
