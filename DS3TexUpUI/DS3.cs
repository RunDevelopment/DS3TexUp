using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using SoulsFormats;
using SixLabors.ImageSharp;
using Pfim;
using SixLabors.ImageSharp.PixelFormats;

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
        public static readonly IReadOnlyCollection<TexId> GroundWithMossTextures
            = DataFile(@"ground-with-moss.json").LoadJsonFile<HashSet<TexId>>();

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
            => CreateExtractedFilesIndexJson(w, DataFile(@"alpha.json"), f => DDSImage.Load(f).GetTransparency());

        public static IReadOnlyDictionary<TexId, RgbaDiff> OriginalColorDiff
            = DataFile(@"original-color-diff.json").LoadJsonFile<Dictionary<TexId, RgbaDiff>>();
        internal static Action<SubProgressToken> CreateOriginalColorDiffIndex(Workspace w)
        {
            return CreateExtractedFilesIndexJson(w, DataFile(@"original-color-diff.json"), f =>
            {
                using var image = DDSImage.Load(f);
                return image.GetMaxAbsDiff();
            });
        }

        public static IReadOnlyDictionary<TexId, Size> OriginalSize
            = DataFile(@"original-size.json").LoadJsonFile<Dictionary<TexId, Size>>();
        internal static Action<SubProgressToken> CreateOriginalSizeIndex(Workspace w)
        {
            return CreateExtractedFilesIndexJson(w, DataFile(@"original-size.json"), f =>
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

        public static IReadOnlyDictionary<TexId, string> GamePath
            = DataFile(@"game-path.json").LoadJsonFile<Dictionary<TexId, string>>();
        internal static Action<SubProgressToken> CreateGamePathIndex(Workspace w)
        {
            return token =>
            {
                var index = w.GetTexFiles(token).ToDictionary(f => f.Id, f => Path.GetRelativePath(w.GameDir, f.GamePath));

                token.SubmitStatus("Saving JSON");
                index.SaveAsJson(DataFile(@"game-path.json"));
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
            = CopiesFromPairs(DataFile(@"copies.json").LoadJsonFile<List<(TexId, TexId)>>());
        internal static Action<SubProgressToken> CreateCopyIndex(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Searching for files");
                var files = Directory.GetFiles(w.ExtractDir, "*.dds", SearchOption.AllDirectories)
                    // ignore all images that are just solid colors
                    .Where(f => !TexId.FromPath(f).IsSolidColor(0.05))
                    .ToArray();

                var index = CopyIndex.Create(token.Reserve(0.5), files);

                var copies = new Dictionary<TexId, List<TexId>>();

                var simCache = new ConcurrentDictionary<(TexId, TexId), bool>();
                Func<TexId, ArrayTextureMap<Rgba32>, TexId, string, bool> isSimilar = (aId, aImage, bId, bFile) =>
                {
                    // same image
                    if (aId == bId) return true;

                    var key = aId < bId ? (aId, bId) : (bId, aId);

                    if (simCache.TryGetValue(key, out var cachedSim)) return cachedSim;

                    var simScore = aImage.GetSimilarityScore(bFile.LoadTextureMap());
                    var sim = simScore.color < 0.04 && simScore.feature < 0.12;

                    simCache.TryAdd(key, sim);
                    return sim;
                };

                token.SubmitStatus($"Looking up {files.Length} files");
                token.ForAllParallel(files, f =>
                {
                    var id = TexId.FromPath(f);
                    var set = new HashSet<TexId>() { id };

                    try
                    {
                        var image = f.LoadTextureMap();

                        var similar = index.GetSimilar(image, 2);
                        if (similar != null)
                        {
                            foreach (var e in similar)
                            {
                                var eId = TexId.FromPath(e.File);
                                if (isSimilar(id, image, eId, e.File))
                                    set.Add(eId);
                            }
                        }
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
                PairsFromCopies(copies).SaveAsJson(DataFile(@"copies.json"));
            };
        }
        internal static Dictionary<TexId, HashSet<TexId>> CopiesFromPairs(IEnumerable<(TexId, TexId)> pairs)
        {
            static HashSet<TexId> NewHashSet(TexId id)
            {
                var set = new HashSet<TexId>();
                set.Add(id);
                return set;
            }

            var copies = new Dictionary<TexId, HashSet<TexId>>();
            foreach (var (a, b) in pairs)
            {
                copies.GetOrAdd(a, NewHashSet).Add(b);
                copies.GetOrAdd(b, NewHashSet).Add(a);
            }
            return copies;
        }
        internal static List<(TexId, TexId)> PairsFromCopies<T>(IReadOnlyDictionary<TexId, T> copies)
            where T : IReadOnlyCollection<TexId>
        {
            static (TexId, TexId) NewPair(TexId a, TexId b) => a <= b ? (a, b) : (b, a);

            var l = copies
                .SelectMany(kv => kv.Value.Select(a => NewPair(a, kv.Key)))
                .Where(p => p.Item1 != p.Item2)
                .ToHashSet()
                .ToList();
            l.Sort();
            return l;
        }

        public static IReadOnlyDictionary<TexId, HashSet<TexId>> LargestCopy
            = DataFile(@"largest-copy.json").LoadJsonFile<Dictionary<TexId, HashSet<TexId>>>();
        public static IReadOnlyDictionary<TexId, TexId> LargestCopyOf
            = LargestCopy.SelectMany(kv => kv.Value.Select(v => (v, kv.Key))).ToDictionary(p => p.v, p => p.Key);
        internal static Action<SubProgressToken> CreateLargestCopyIndex(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus($"Searching for files");
                var ids = Directory.GetFiles(w.ExtractDir, "*.dds", SearchOption.AllDirectories).Select(TexId.FromPath).ToList();
                ids.Sort();

                token.SubmitStatus($"Indexing");
                var largest = new Dictionary<TexId, List<TexId>>();
                token.ForAllParallel(ids, id =>
                {
                    var l = id.ComputeLargerCopy(w);
                    if (l != null)
                    {
                        lock (largest)
                        {
                            largest.GetOrAdd(l.Value).Add(id);
                        }
                    }
                });

                foreach (var (_, l) in largest)
                    l.Sort();

                token.SubmitStatus("Saving JSON");
                largest.SaveAsJson(DataFile(@"largest-copy.json"));
            };
        }

        public static IReadOnlyDictionary<TexId, Dictionary<string, HashSet<string>>> UsedBy
            = DataFile(@"usage.json").LoadJsonFile<Dictionary<TexId, Dictionary<string, HashSet<string>>>>();
        internal static Action<SubProgressToken> CreateTexUsage()
        {
            return token =>
            {
                var usage = new Dictionary<TexId, Dictionary<string, HashSet<string>>>();

                foreach (var info in ReadAllFlverMaterialInfo())
                {
                    foreach (var mat in info.Materials)
                    {
                        foreach (var tex in mat.Textures)
                        {
                            var id = TexId.FromTexture(tex, info.FlverPath);
                            if (id != null)
                                usage.GetOrAdd(id.Value).GetOrAdd(info.FlverPath).Add(mat.Name);
                        }
                    }
                }

                token.SubmitStatus("Saving copies JSON");
                usage.SaveAsJson(DataFile(@"usage.json"));
            };
        }

        public static IReadOnlyCollection<TexId> Unused
            = DataFile(@"unused.json").LoadJsonFile<HashSet<TexId>>();
        internal static Action<SubProgressToken> CreateUnused(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus($"Searching for files");
                var files = Directory.GetFiles(w.ExtractDir, "*.dds", SearchOption.AllDirectories);

                var unused = files.Select(TexId.FromPath).Where(id => !UsedBy.ContainsKey(id)).ToList();
                unused.Sort();

                token.SubmitStatus("Saving copies JSON");
                unused.SaveAsJson(DataFile(@"unused.json"));
            };
        }

        public static IReadOnlyDictionary<TexId, TexId> NormalAlbedo
            = DataFile(@"normal-albedo.json").LoadJsonFile<Dictionary<TexId, TexId>>();
        internal static Action<SubProgressToken> CreateNormalAlbedoIndex()
        {
            return token =>
            {
                var index = new Dictionary<TexId, List<TexId>>();
                Action<TexId?, TexId?> AddToIndex = (normal, albedo) =>
                {
                    if (normal != null && albedo != null)
                    {
                        var n = normal.Value;
                        var a = albedo.Value;
                        a = a.GetLargestCopy() ?? a;
                        if (!n.IsUnwanted() && !a.IsUnwanted() && !n.IsSolidColor(0.05) && !a.IsSolidColor(0.05))
                        {
                            if (DS3.OriginalSize.TryGetValue(n, out var nSize) && DS3.OriginalSize.TryGetValue(a, out var aSize))
                            {
                                if (SizeRatio.Of(nSize) == SizeRatio.Of(aSize))
                                {
                                    index.GetOrAdd(normal.Value).Add(albedo.Value);
                                }
                            }
                        }
                    }
                };

                token.SubmitStatus($"Analysing flver files");
                foreach (var info in DS3.ReadAllFlverMaterialInfo())
                {
                    var a = new List<(TexId? id, string type)>();
                    var n = new List<(TexId? id, string type)>();
                    foreach (var mat in info.Materials)
                    {
                        a.Clear();
                        n.Clear();
                        foreach (var tex in mat.Textures)
                        {
                            var i = TexId.FromTexture(tex, info.FlverPath);
                            var kind = DS3.TextureTypeToTexKind.GetOrDefault(tex.Type, TexKind.Unknown);
                            if (kind == TexKind.Albedo)
                                a.Add((i, tex.Type));
                            else if (kind == TexKind.Normal)
                                n.Add((i, tex.Type));
                        }

                        if (a.Count == 0 || n.Count == 0) continue;

                        if (a.Count == n.Count && a.Count >= 2)
                        {
                            // There is a pattern where they types would end with _0, _1, and so on.
                            static bool EndsWithNumber(string s)
                                => s.Length >= 2 && s[^2] == '_' && s[^1] >= '0' && s[^1] <= '9';

                            if (a.Select(p => p.type).All(EndsWithNumber) &&
                                n.Select(p => p.type).All(EndsWithNumber))
                            {
                                a.Sort((a, b) => a.type[^1].CompareTo(b.type[^1]));
                                n.Sort((a, b) => a.type[^1].CompareTo(b.type[^1]));

                                if (a.Where((p, i) => p.type[^1] - '0' == i).Count() == a.Count &&
                                    n.Where((p, i) => p.type[^1] - '0' == i).Count() == a.Count)
                                {
                                    for (int i = 0; i < a.Count; i++)
                                        AddToIndex(n[i].id, a[i].id);
                                    continue;
                                }
                            }
                        }

                        if (a.Count == 1 && n.Count == 1)
                        {
                            // simple case
                            AddToIndex(n[0].id, a[0].id);
                            continue;
                        }

                        if (a.Count == 2 && n.Count == 2 &&
                            a[0].type.EndsWith("Texture") && n[0].type.EndsWith("Texture") &&
                            a[1].type.EndsWith("Texture2") && n[1].type.EndsWith("Texture2"))
                        {
                            AddToIndex(n[0].id, a[0].id);
                            AddToIndex(n[1].id, a[1].id);
                            continue;
                        }

                        if (a.Count == 2 && n.Count == 3 &&
                            mat.MTD.Contains("_Water", StringComparison.OrdinalIgnoreCase) &&
                            a[0].type.EndsWith("Texture") && n[0].type.EndsWith("Texture") &&
                            a[1].type.EndsWith("Texture2") && n[1].type.EndsWith("Texture2") &&
                            n[2].type.EndsWith("Texture3"))
                        {
                            AddToIndex(n[0].id, a[0].id);
                            AddToIndex(n[1].id, a[1].id);
                            continue;
                        }
                    }
                }

                token.SubmitStatus($"Analysing texture files");
                var forbidden = new HashSet<TexId>() {
                    new TexId("m32/m32_00_o4520_3_n"),
                    new TexId("m37/M37_00_Outside_CedPlaceHolder_n"),
                };
                static TexId GetAlbedoByName(TexId n)
                {
                    return new TexId(n.Category, n.Name.Slice(0, n.Name.Length - 2).ToString() + "_a");
                }
                foreach (var id in DS3.OriginalSize.Keys)
                {
                    if (id.GetTexKind() == TexKind.Normal && id.Name.EndsWith("_n") && !index.ContainsKey(id) && !forbidden.Contains(id))
                        AddToIndex(id, GetAlbedoByName(id));
                }

                // token.SubmitStatus("Saving JSON");
                // index.SaveAsJson(DataFile(@"normal-albedo.json"));

                token.SubmitStatus($"Categorizing pairings");
                var certain = new Dictionary<TexId, TexId>();
                var ambiguous = new Dictionary<TexId, Dictionary<TexId, int>>();

                foreach (var (n, ids) in index)
                {
                    var set = ids.ToHashSet();
                    if (set.Count == 1)
                    {
                        certain[n] = ids[0];
                    }
                    else
                    {
                        var byCount = ids.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

                        var maxCount = byCount.Values.Max();
                        var maxA = byCount.Where(kv => kv.Value == maxCount).First().Key;
                        var certainPercentage = (maxCount - 1) / (double)ids.Count;

                        if (certainPercentage >= 0.65)
                        {
                            certain[n] = maxA;
                            continue;
                        }

                        var name = GetAlbedoByName(n);
                        var maxSize = set.Select(id => DS3.OriginalSize[id].Width).Max();
                        if (byCount.TryGetValue(name, out var nameCount) && (nameCount == maxCount || DS3.OriginalSize[name].Width == maxSize))
                        {
                            certain[n] = name;
                            continue;
                        }

                        ambiguous[n] = byCount;
                    }
                }

                token.SubmitStatus("Saving JSON");
                certain.SaveAsJson(DataFile(@"normal-albedo-auto.json"));
                ambiguous.SaveAsJson(DataFile(@"normal-albedo-manual.json"));
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
