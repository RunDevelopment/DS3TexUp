using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using SoulsFormats;
using SixLabors.ImageSharp;
using Pfim;

#nullable enable

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

        public static readonly IReadOnlyCollection<TexId> GroundTextures
            = DataFile(@"ground.json").LoadJsonFile<HashSet<TexId>>();

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
            => CreateExtractedFilesIndexJson(w, DataFile(@"original-format.json"), f => DDSImage.Load(f).GetTransparency());

        public static IReadOnlyDictionary<TexId, Size> OriginalSize
            = DataFile(@"original-size.json").LoadJsonFile<Dictionary<TexId, Size>>();
        internal static Action<SubProgressToken> CreateOriginalSizeIndex(Workspace w)
        {
            return CreateExtractedFilesIndexJson(w, DataFile(@"original-format.json"), f =>
            {
                var (header, _) = f.ReadDdsHeader();
                return new Size((int)header.Width, (int)header.Height);
            });
        }

        public static IReadOnlyDictionary<TexId, DDSFormat> OriginalFormat
            = DataFile(@"original-format.json").LoadJsonFile<Dictionary<TexId, DDSFormat>>();
        internal static Action<SubProgressToken> CreateOriginalFormatIndex(Workspace w)
            => CreateExtractedFilesIndexJson(w, DataFile(@"original-format.json"), f => f.ReadDdsHeader().GetFormat());
        internal static Action<SubProgressToken> CreateFormatsGroupedByTexKind()
        {
            return token =>
            {
                var list = OriginalFormat
                    .GroupBy(p => p.Key.GetTexKind())
                    .OrderBy(p => (int)p.Key)
                    .Select(g =>
                    {
                        return new
                        {
                            Type = g.Key.ToString(),
                            Fromats = g
                                .GroupBy(p => p.Value)
                                .Select(g => new { Fromat = g.Key, Count = g.Count(), Textures = g.Select(p => p.Key).OrderBy(id => id).ToList() })
                                .OrderByDescending(t => t.Count)
                                .ToList()
                        };
                    })
                    .ToList();

                token.SubmitStatus("Saving formats");
                list.SaveAsJson(DataFile(@"format-by-tex-kind.json"));
            };
        }

        public static IReadOnlyDictionary<string, TexKind> TextureTypeToTexKind
            = DataFile(@"texture-type-to-tex-kind.json").LoadJsonFile<Dictionary<string, TexKind>>();

        // public static IReadOnlyDictionary<TexId, DDSFormat> OutputFormat
        //     = DataFile(@"output-format.json").LoadJsonFile<Dictionary<TexId, DDSFormat>>();
        internal static Action<SubProgressToken> CreateOutputFormatIndex(Workspace w)
        {
            static DDSFormat GetOutputNormalFormat(DDSFormat format)
            {
                // Always use BC7 for normals!
                if (format.DxgiFormat == DxgiFormat.BC1_UNORM_SRGB) return DxgiFormat.BC7_UNORM_SRGB;
                if (format.DxgiFormat == DxgiFormat.BC1_UNORM) return DxgiFormat.BC7_UNORM;

                // TODO: The other formats
                return format;
            }

            static DDSFormat GetOutputFormat(DDSFormat format, TexKind kind)
            {
                // BC7 will always achieve better quality with the same memory
                if (format.DxgiFormat == DxgiFormat.BC3_UNORM) return DxgiFormat.BC7_UNORM;
                if (format.DxgiFormat == DxgiFormat.BC3_UNORM_SRGB) return DxgiFormat.BC7_UNORM_SRGB;

                return kind switch
                {
                    // TODO: Other texture kinds need attention too!
                    TexKind.Normal => GetOutputNormalFormat(format),
                    _ => format
                };
            }

            return token =>
            {
                var e = OriginalFormat
                    .Select(kv => new KeyValuePair<TexId, DDSFormat>(kv.Key, GetOutputFormat(kv.Value, kv.Key.GetTexKind())))
                    .ToList();

                token.SubmitStatus("Saving formats");
                new Dictionary<TexId, DDSFormat>(e).SaveAsJson(DataFile(@"output-format.json"));
            };
        }

        public static IReadOnlyDictionary<TexId, HashSet<TexId>> Copies
            = DataFile(@"copies.json").LoadJsonFile<Dictionary<TexId, HashSet<TexId>>>();
        internal static Action<SubProgressToken> CreateCopyIndex(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Searching for files");
                var files = Directory.GetFiles(w.ExtractDir, "*.dds", SearchOption.AllDirectories);

                var index = CopyIndex.Create(token.Reserve(0.5), files);

                var copies = new Dictionary<TexId, List<TexId>>();

                token.SubmitStatus($"Looking up {files.Length} files");
                token.ForAllParallel(files, f =>
                {
                    var id = TexId.FromPath(f);
                    var set = new HashSet<TexId>() { id };

                    try
                    {
                        var similar = index.GetSimilar(f);
                        if (similar != null)
                            set.UnionWith(similar.Select(e => TexId.FromPath(e.File)));
                    }
                    catch (System.Exception)
                    {
                        // ignore
                    }

                    var kind = id.GetTexKind();
                    set.RemoveWhere(i => i.GetTexKind() != kind);

                    var result = new List<TexId>(set);
                    result.Sort();

                    lock (copies)
                    {
                        copies[id] = result;
                    }
                });

                token.SubmitStatus("Saving copies JSON");
                copies.SaveAsJson(DataFile(@"copies.json"));
            };
        }

        public static IEnumerable<FlverMaterialInfo> ReadAllFlverMaterialInfo()
        {
            foreach (var file in Directory.GetFiles(DataFile(@"materials"), "*.json"))
                foreach (var item in file.LoadJsonFile<List<FlverMaterialInfo>>())
                    yield return item;
        }


        private static Action<SubProgressToken> CreateExtractedFilesIndexJson<T>(Workspace w, string outputFile, Func<string, T> valueSector)
        {
            return token =>
            {
                token.SubmitStatus($"Searching for files");
                var files = Directory.GetFiles(w.ExtractDir, "*.dds", SearchOption.AllDirectories);

                token.SubmitStatus($"Indexing {files.Length} files");
                var index = new Dictionary<TexId, T>();
                token.ForAllParallel(files, f =>
                {
                    try
                    {
                        var id = TexId.FromPath(f);
                        var value = valueSector(f);
                        lock (index)
                        {
                            index[id] = value;
                        }
                    }
                    catch (System.Exception)
                    {
                        // ignore
                    }
                });

                token.SubmitStatus($"Saving index");
                index.SaveAsJson(outputFile);
            };
        }
    }

    public struct FlverMaterialInfo
    {
        public string FlverPath { get; set; }
        public List<FLVER2.GXList> GXLists { get; set; }
        public List<FLVER2.Material> Materials { get; set; }
    }
}
