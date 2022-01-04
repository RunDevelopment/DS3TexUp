using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using SoulsFormats;

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

        private static string DataFile(string name)
        {
            return Path.Join(AppDomain.CurrentDomain.BaseDirectory, "data", name);
        }

        public static readonly IReadOnlyList<string> Parts
            = DataFile(@"parts.json").LoadJsonFile<string[]>();

        public static readonly IReadOnlyDictionary<TexId, TexKind> KnownTexKinds
            = DataFile(@"tex-kinds.json").LoadJsonFile<Dictionary<TexId, TexKind>>();
        internal static Action<SubProgressToken> CreateKnownTexKindsIndex()
        {
            static IEnumerable<(TexId id, TexKind kind)> Selector(FlverMaterialInfo info)
            {
                foreach (var mat in info.Materials)
                {
                    foreach (var tex in mat.Textures)
                    {
                        var id = TexId.FromTexture(tex, info.FlverPath);
                        var kind = TextureTypeToTexKind.GetOrDefault(tex.Type, TexKind.Unknown);
                        if (id != null && kind != TexKind.Unknown)
                            yield return (id.Value, kind);
                    }
                }
            }

            return token =>
            {
                token.SubmitStatus($"Creating index");

                var grouped = ReadAllFlverMaterialInfo()
                    .SelectMany(Selector)
                    .GroupBy(p => p.id);

                var result = new Dictionary<TexId, TexKind>();
                foreach (var group in grouped)
                {
                    // Get the most common texture kind
                    var kind = group
                            .Select(p => p.kind)
                            .GroupBy(k => k)
                            .Select(g => (g.Key, g.Count()))
                            .Aggregate((a, b) => a.Item2 > b.Item2 ? a : b)
                            .Key;

                    result[group.Key] = kind;
                }

                token.SubmitStatus($"Saving index");
                result.SaveAsJson(DataFile(@"tex-kinds.json"));
            };
        }

        public static IReadOnlyDictionary<TexId, TransparencyKind> Transparency
            = DataFile(@"alpha.json").LoadJsonFile<Dictionary<TexId, TransparencyKind>>();
        internal static Action<SubProgressToken> CreateTransparencyIndex(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus($"Searching for files");
                var files = Directory.GetFiles(w.ExtractDir, "*.dds", SearchOption.AllDirectories);

                token.SubmitStatus($"Indexing {files.Length} files");
                var index = new Dictionary<TexId, TransparencyKind>();
                token.ForAllParallel(files, f =>
                {
                    try
                    {
                        var id = TexId.FromPath(f);
                        var kind = DDSImage.Load(f).GetTransparency();
                        lock (index)
                        {
                            index[id] = kind;
                        }
                    }
                    catch (System.Exception)
                    {
                        // ignore
                    }
                });

                token.SubmitStatus($"Saving index");
                index.SaveAsJson(DataFile(@"alpha.json"));
            };
        }

        public static IReadOnlyDictionary<string, TexKind> TextureTypeToTexKind
            = DataFile(@"texture-type-to-tex-kind.json").LoadJsonFile<Dictionary<string, TexKind>>();

        public static IEnumerable<FlverMaterialInfo> ReadAllFlverMaterialInfo()
        {
            foreach (var file in Directory.GetFiles(DataFile(@"materials"), "*.json"))
                foreach (var item in file.LoadJsonFile<List<FlverMaterialInfo>>())
                    yield return item;
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

    public struct FlverMaterialInfo
    {
        public string FlverPath { get; set; }
        public List<FLVER2.GXList> GXLists { get; set; }
        public List<FLVER2.Material> Materials { get; set; }
    }
}
