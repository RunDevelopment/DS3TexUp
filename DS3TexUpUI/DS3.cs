using System;
using System.Collections;
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

        public static readonly IReadOnlyCollection<TexId> SolidColor
            = DataFile(@"solid-color.json").LoadJsonFile<HashSet<TexId>>();

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
                    .Where(p => !Unused.Contains(p.Key))
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

        public static IReadOnlyDictionary<string, HashSet<TexId>> Homographs
            = DataFile(@"homographs.json").LoadJsonFile<Dictionary<string, HashSet<TexId>>>();
        internal static Action<SubProgressToken> CreateHomographIndex(Workspace w)
        {
            return token =>
            {
                var homographs = OriginalSize.Keys
                    .GroupBy(id => id.GetInGameName())
                    .Select(g =>
                    {
                        var l = g.ToList();
                        l.Sort();
                        return (g.Key, l);
                    })
                    .Where(p => p.l.Count >= 2)
                    .ToDictionary(p => p.Key, p => p.l);

                token.SubmitStatus("Saving JSON");
                homographs.SaveAsJson(DataFile(@"homographs.json"));
            };
        }

        public static IReadOnlyDictionary<string, TexKind> TextureTypeToTexKind
            = DataFile(@"texture-type-to-tex-kind.json").LoadJsonFile<Dictionary<string, TexKind>>();

        public static IReadOnlyDictionary<TexId, DDSFormat> OutputFormat
            = DataFile(@"output-format.json").LoadJsonFile<Dictionary<TexId, DDSFormat>>();
        internal static Action<SubProgressToken> CreateOutputFormatIndex(Workspace w)
        {
            static DDSFormat GetOutputFormat(DDSFormat format, TexKind kind)
            {
                var dx = format.ToDX10();

                // BC7 will always achieve better quality with the same memory
                if (dx == DxgiFormat.BC3_UNORM) return DxgiFormat.BC7_UNORM;
                if (dx == DxgiFormat.BC3_UNORM_SRGB) return DxgiFormat.BC7_UNORM_SRGB;

                // Some formats should not use BC1
                if (kind == TexKind.Normal || kind == TexKind.Height || kind == TexKind.VertexOffset)
                {
                    if (dx == DxgiFormat.BC1_UNORM_SRGB) return DxgiFormat.BC7_UNORM_SRGB;
                    if (dx == DxgiFormat.BC1_UNORM) return DxgiFormat.BC7_UNORM;
                }

                return dx == DxgiFormat.UNKNOWN ? format : dx;
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

        public static IReadOnlyDictionary<TexId, ColorCode6x6> ColorCode
            = DataFile(@"color-code.json").LoadJsonFile<Dictionary<TexId, ColorCode6x6>>();
        internal static Action<SubProgressToken> CreateColorCodeIndex()
        {
            return token =>
            {
                token.SubmitStatus("Selecting textures");
                var ids = OriginalSize.Keys
                    .Where(id => id.GetTexKind() == TexKind.Albedo)
                    .Where(id => !Unused.Contains(id))
                    .ToList();
                ids.Sort();

                token.SubmitStatus("Creating index");
                var index = new Dictionary<TexId, ColorCode6x6>();
                var set = new BitArray((int)ColorCode6x6.Max);
                token.ForAll(ids, id =>
                {
                    var i = unchecked((uint)id.GetHashCode());
                    i ^= i >> 16;
                    var c = new ColorCode6x6(i);

                    // probe until we find a free id
                    for (var t = 0u; set[(int)c.Number]; t++)
                    {
                        i += t * 2 + 1;
                        c = new ColorCode6x6(i);
                    }

                    set[(int)c.Number] = true;
                    index[id] = c;
                });

                token.SubmitStatus("Saving JSON");
                index.SaveAsJson(DataFile(@"color-code.json"));
            };
        }

        public static IReadOnlyDictionary<TexId, HashSet<TexId>> CopiesUncertain
            = CopiesFromPairs(DataFile(@"copies-uncertain.json").LoadJsonFile<List<(TexId, TexId)>>());
        internal static Action<SubProgressToken> CreateCopyIndex(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Searching for files");
                var files = Directory.GetFiles(w.ExtractDir, "*.dds", SearchOption.AllDirectories)
                    // ignore all images that are just solid colors
                    .Where(f => !TexId.FromPath(f).IsSolidColor())
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
                    var sim = simScore.color < 0.055 && simScore.feature < 0.24;

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

                        // small images suffer more from compression artifacts, so we want to given them a boost
                        var spread = image.Count <= 64 * 64 ? 8 : image.Count <= 128 * 128 ? 6 : 4;
                        var similar = index.GetSimilar(image, (byte)spread);
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
                PairsFromCopies(copies).SaveAsJson(DataFile(@"copies-uncertain.json"));
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

        public static IReadOnlyDictionary<TexId, HashSet<TexId>> CopiesIdentical
            = CopiesFromPairs(DataFile(@"copies-identical.json").LoadJsonFile<List<(TexId, TexId)>>());
        internal static Action<SubProgressToken> CreateIdenticalIndex(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Finding identical");
                var identical = new Dictionary<TexId, HashSet<TexId>>();

                var cache = new ConcurrentDictionary<(TexId, TexId), bool>();
                bool AreIdentical(TexId a, TexId b)
                {
                    if (a == b) return true;
                    if (DS3.OriginalSize[a].Width != DS3.OriginalSize[b].Width) return false;

                    if (a > b) (b, a) = (a, b);
                    var key = (a, b);

                    if (cache!.TryGetValue(key, out var cachedResult)) return cachedResult;

                    var imageA = w.GetExtractPath(a).LoadTextureMap();
                    var imageB = w.GetExtractPath(b).LoadTextureMap();
                    var identical = true;
                    for (int i = 0; i < imageA.Count; i++)
                    {
                        var pa = imageA[i];
                        var pb = imageB[i];

                        var diffR = Math.Abs(pa.R - pb.R);
                        var diffG = Math.Abs(pa.G - pb.G);
                        var diffB = Math.Abs(pa.B - pb.B);
                        var diffA = Math.Abs(pa.A - pb.A);

                        const int MaxDiff = 2;
                        if (diffR > MaxDiff || diffG > MaxDiff || diffB > MaxDiff || diffA > MaxDiff)
                        {
                            identical = false;
                            break;
                        }
                    }

                    cache[key] = identical;
                    return identical;
                }

                token.ForAllParallel(CopiesUncertain.Values, copies =>
                {
                    foreach (var a in copies)
                    {
                        foreach (var b in copies)
                        {
                            if (AreIdentical(a, b))
                            {
                                lock (identical)
                                {
                                    static HashSet<TexId> NewHashSet(TexId id) => new HashSet<TexId>() { id };
                                    identical.GetOrAdd(a, NewHashSet).Add(b);
                                    identical.GetOrAdd(b, NewHashSet).Add(a);
                                }
                            }
                        }
                    }
                });

                token.SubmitStatus("Saving JSON");
                PairsFromCopies(identical).SaveAsJson(DataFile(@"copies-identical.json"));
            };
        }

        public static IReadOnlyDictionary<TexId, HashSet<TexId>> CopiesCertain
            = CopiesFromPairs(DataFile(@"copies-certain.json").LoadJsonFile<List<(TexId, TexId)>>());
        public static IReadOnlyDictionary<TexId, HashSet<TexId>> CopiesRejected
            = CopiesFromPairs(DataFile(@"copies-rejected.json").LoadJsonFile<List<(TexId, TexId)>>());
        private static readonly string _sccDirectory = @"C:\DS3TexUp\scc";
        internal static Action<SubProgressToken> CreateSCCDirectory(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Getting SCC");
                var scc = StronglyConnectedComponents.Find(DS3.OriginalSize.Keys, id =>
                {
                    var l = new List<TexId>() { id };
                    if (DS3.CopiesUncertain.TryGetValue(id, out var copies))
                    {
                        var without = DS3.CopiesRejected.GetOrNew(id).SelectMany(i => DS3.CopiesCertain.GetOrNew(i));
                        l.AddRange(copies.Except(without));
                    }
                    return l;
                }).Where(l => l.Count >= 2).OrderByDescending(l => l.Count).ToList();

                token.SubmitStatus("Copying files");
                token.ForAllParallel(scc.Select((x, i) => (x, i)), pair =>
                {
                    var (scc, i) = pair;

                    // Remove identical textures
                    static TexId MapTo(TexId id) => id.GetRepresentative();
                    scc = scc.Select(MapTo).ToHashSet().ToList();

                    // only one texture after identical textures were removed
                    if (scc.Count < 2) return;

                    var dir = Path.Join(_sccDirectory, "" + i);
                    Directory.CreateDirectory(dir);

                    var largest = scc.Select(id => DS3.OriginalSize[id].Width).Max();

                    foreach (var id in scc)
                    {
                        token.CheckCanceled();

                        var source = w.GetExtractPath(id);
                        var target = Path.Join(dir, $"{id.Category.ToString()}-{Path.GetFileName(source)}");
                        if (DS3.OriginalSize[id].Width < largest)
                        {
                            var image = source.LoadTextureMap();
                            image.UpSample(largest / image.Width).SaveAsPng(Path.ChangeExtension(target, "png"));
                        }
                        else
                        {
                            File.Copy(source, target);
                        }
                    }
                });
            };
        }
        internal static Action<SubProgressToken> ReadSCCDirectory(Workspace w)
        {
            return token =>
            {
                var files = Directory.GetFiles(_sccDirectory, "*", SearchOption.AllDirectories);

                static TexId SCCFilenameToTexId(string file)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    name = name.Replace('-', '/');
                    return new TexId(name);
                }
                var components = files
                    .GroupBy(f => Path.GetDirectoryName(f))
                    .Select(g => g.Select(SCCFilenameToTexId).ToList())
                    .SelectMany(l => l.Select(i => (l, i)))
                    .ToDictionary(p => p.i, p => p.l);

                foreach (var (id, others) in DS3.CopiesCertain)
                {
                    var c = components.GetOrAdd(id);
                    c.AddRange(others.Except(c));
                }

                var pairs = DS3.PairsFromCopies(components);
                pairs.SaveAsJson(DataFile(@"copies-certain.json"));

                var scc = StronglyConnectedComponents.Find(DS3.OriginalSize.Keys, id =>
                {
                    var l = new List<TexId>() { id };
                    if (DS3.CopiesUncertain.TryGetValue(id, out var copies))
                    {
                        l.AddRange(copies);
                    }
                    return l;
                }).SelectMany(l => l.Select(i => (l, i))).ToDictionary(p => p.i, p => p.l);
                var sccPairs = DS3.PairsFromCopies(scc);

                var rejected = sccPairs.Except(pairs).ToList();
                rejected.SaveAsJson(DataFile(@"copies-rejected.json"));
            };
        }

        public static IReadOnlyDictionary<TexId, HashSet<TexId>> LargestCopy
            = DataFile(@"largest-copy.json").LoadJsonFile<Dictionary<TexId, HashSet<TexId>>>();
        public static IReadOnlyDictionary<TexId, TexId> LargestCopyOf
            = LargestCopy.SelectMany(kv => kv.Value.Select(v => (v, kv.Key))).ToDictionary(p => p.v, p => p.Key);

        public static IReadOnlyDictionary<TexId, TexId> RepresentativeOf
            = DataFile(@"representative.json").LoadJsonFile<Dictionary<TexId, TexId>>();
        public static IReadOnlyCollection<TexId> Representatives = RepresentativeOf.Values.ToHashSet();
        internal static Action<SubProgressToken> CreateRepresentativeIndex(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus($"Searching for files");
                var ids = DS3.OriginalSize.Keys.ToList();
                ids.Sort();

                token.SubmitStatus($"Indexing");
                var representative = new Dictionary<TexId, TexId>();
                token.ForAllParallel(ids, id =>
                {
                    if (!CopiesCertain.TryGetValue(id, out var othersSet) || othersSet.Count == 0) return;

                    var others = othersSet.ToList();
                    others.Sort(CompareIdsByQuality);

                    var r = others.Last();
                    if (r != id)
                    {
                        lock (representative)
                        {
                            representative[id] = r;
                        }
                    }
                });

                token.SubmitStatus("Saving JSON");
                representative.SaveAsJson(DataFile(@"representative.json"));
            };
        }
        public static int CompareIdsByQuality(TexId a, TexId b)
        {
            var q = CompareQuality(a, b);
            if (q != 0) return q;

            // try not to pick one that isn't used in game
            var u = IsUsedInGame(a).CompareTo(IsUsedInGame(b));
            if (u != 0) return u;

            // if it doesn't matter which one we pick, pick the one with the smaller ID.
            return -a.CompareTo(b);


            static bool IsUsedInGame(TexId id)
            {
                return DS3.UsedBy.ContainsKey(id);
            }
            static int CompareQuality(TexId a, TexId b)
            {
                if (DS3.OriginalSize.TryGetValue(a, out var aSize) && DS3.OriginalSize.TryGetValue(b, out var bSize))
                {
                    var s = aSize.Width.CompareTo(bSize.Width);
                    if (s != 0) return s;
                }

                static int? GetCompareNumber(DDSFormat format)
                {
                    switch (format.FourCC)
                    {
                        case CompressionAlgorithm.D3DFMT_DXT1:
                            return 1;
                        case CompressionAlgorithm.D3DFMT_DXT5:
                            return 3;
                        case CompressionAlgorithm.ATI1:
                            return 4;
                        case CompressionAlgorithm.ATI2:
                            return 5;
                        case CompressionAlgorithm.DX10:
                            switch (format.DxgiFormat)
                            {
                                case DxgiFormat.BC1_TYPELESS:
                                case DxgiFormat.BC1_UNORM_SRGB:
                                case DxgiFormat.BC1_UNORM:
                                    return 1;
                                case DxgiFormat.BC3_UNORM_SRGB:
                                    return 3;
                                case DxgiFormat.BC4_SNORM:
                                case DxgiFormat.BC4_TYPELESS:
                                case DxgiFormat.BC4_UNORM:
                                    return 4;
                                case DxgiFormat.BC5_SNORM:
                                case DxgiFormat.BC5_TYPELESS:
                                case DxgiFormat.BC5_UNORM:
                                    return 5;
                                case DxgiFormat.BC7_UNORM:
                                case DxgiFormat.BC7_UNORM_SRGB:
                                    return 7;
                                case DxgiFormat.B8G8R8A8_UNORM_SRGB:
                                case DxgiFormat.B8G8R8X8_UNORM_SRGB:
                                case DxgiFormat.B5G5R5A1_UNORM:
                                    // uncompressed
                                    return 10;
                                default:
                                    return null;
                            }

                        default:
                            return null;
                    }
                }

                if (DS3.OriginalFormat.TryGetValue(a, out var aFormat) && DS3.OriginalFormat.TryGetValue(b, out var bFormat))
                {
                    var af = GetCompareNumber(aFormat);
                    var bf = GetCompareNumber(bFormat);
                    if (af != null && bf != null && af != bf)
                    {
                        return af.Value.CompareTo(bf.Value);
                    }
                }

                return 0;
            }
        }

        public static IReadOnlyDictionary<TexId, HashSet<TexId>> AlphaCopiesUncertain
            = CopiesFromPairs(DataFile(@"alpha-copies-uncertain.json").LoadJsonFile<List<(TexId, TexId)>>());
        internal static Action<SubProgressToken> CreateAlphaCopyIndex(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Searching for files");
                var files = Directory.GetFiles(w.ExtractDir, "*.dds", SearchOption.AllDirectories)
                    // ignore all images that are just solid colors
                    .Where(f => !TexId.FromPath(f).IsSolidColor())
                    // ignore all images that aren't transparanet
                    .Where(f =>
                    {
                        var t = TexId.FromPath(f).GetTransparency();
                        return t == TransparencyKind.Binary || t == TransparencyKind.Full;
                    })
                    .ToArray();

                var index = CopyIndex.Create(token.Reserve(0.5), files, r => new AlphaImageHasher(r));

                var copies = new Dictionary<TexId, List<TexId>>();

                token.SubmitStatus($"Looking up {files.Length} files");
                token.ForAllParallel(files, f =>
                {
                    var id = TexId.FromPath(f);
                    var set = new HashSet<TexId>() { id };

                    try
                    {
                        var image = f.LoadTextureMap();

                        // small images suffer more from compression artifacts, so we want to given them a boost
                        var spread = image.Count <= 64 * 64 ? 12 : 8;
                        var similar = index.GetSimilar(image, (byte)spread);
                        if (similar != null)
                        {
                            foreach (var e in similar)
                                set.Add(TexId.FromPath(e.File));
                        }
                    }
                    catch (System.Exception)
                    {
                        // ignore
                    }

                    var result = new List<TexId>(set);
                    result.Sort();

                    lock (copies)
                    {
                        copies[id] = result;
                    }
                });

                token.SubmitStatus("Saving JSON");
                PairsFromCopies(copies).SaveAsJson(DataFile(@"alpha-copies-uncertain.json"));
            };
        }
        public static IReadOnlyDictionary<TexId, HashSet<TexId>> AlphaCopiesIdentical
            = CopiesFromPairs(DataFile(@"alpha-copies-identical.json").LoadJsonFile<List<(TexId, TexId)>>());
        internal static Action<SubProgressToken> CreateAlphaIdenticalIndex(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Finding identical");
                var identical = new Dictionary<TexId, HashSet<TexId>>();

                var cache = new ConcurrentDictionary<(TexId, TexId), bool>();
                bool AreIdentical(TexId a, TexId b)
                {
                    if (a == b) return true;
                    if (DS3.OriginalSize[a].Width != DS3.OriginalSize[b].Width) return false;

                    if (a > b) (b, a) = (a, b);
                    var key = (a, b);

                    if (cache!.TryGetValue(key, out var cachedResult)) return cachedResult;

                    var imageA = w.GetExtractPath(a).LoadTextureMap();
                    var imageB = w.GetExtractPath(b).LoadTextureMap();
                    var identical = true;
                    for (int i = 0; i < imageA.Count; i++)
                    {
                        var pa = imageA[i];
                        var pb = imageB[i];

                        var diffA = Math.Abs(imageA[i].A - imageB[i].A);

                        const int MaxDiff = 2;
                        if (diffA > MaxDiff)
                        {
                            identical = false;
                            break;
                        }
                    }

                    cache[key] = identical;
                    return identical;
                }

                token.ForAllParallel(AlphaCopiesUncertain.Values, copies =>
                {
                    foreach (var a in copies)
                    {
                        foreach (var b in copies)
                        {
                            if (AreIdentical(a, b))
                            {
                                lock (identical)
                                {
                                    static HashSet<TexId> NewHashSet(TexId id) => new HashSet<TexId>() { id };
                                    identical.GetOrAdd(a, NewHashSet).Add(b);
                                    identical.GetOrAdd(b, NewHashSet).Add(a);
                                }
                            }
                        }
                    }
                });

                token.SubmitStatus("Saving JSON");
                PairsFromCopies(identical).SaveAsJson(DataFile(@"alpha-copies-identical.json"));
            };
        }
        public static IReadOnlyDictionary<TexId, HashSet<TexId>> AlphaCopiesCertain
            = CopiesFromPairs(DataFile(@"alpha-copies-certain.json").LoadJsonFile<List<(TexId, TexId)>>());
        public static IReadOnlyDictionary<TexId, HashSet<TexId>> AlphaCopiesRejected
            = CopiesFromPairs(DataFile(@"alpha-copies-rejected.json").LoadJsonFile<List<(TexId, TexId)>>());
        internal static Action<SubProgressToken> CreateAlphaSCCDirectory(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Getting SCC");
                var scc = StronglyConnectedComponents.Find(DS3.OriginalSize.Keys, id =>
                {
                    var l = new List<TexId>() { id };
                    if (DS3.AlphaCopiesUncertain.TryGetValue(id, out var copies))
                    {
                        var without = DS3.AlphaCopiesRejected.GetOrNew(id).SelectMany(i => DS3.AlphaCopiesCertain.GetOrNew(i));
                        l.AddRange(copies.Except(without));
                    }
                    return l;
                }).Where(l => l.Count >= 2).OrderByDescending(l => l.Count).ToList();

                token.SubmitStatus("Copying files");
                token.ForAllParallel(scc.Select((x, i) => (x, i)), pair =>
                {
                    var (scc, i) = pair;

                    // Remove identical textures
                    static TexId MapTo(TexId id)
                    {
                        if (DS3.AlphaCopiesCertain.TryGetValue(id, out var similar) && similar.Count > 0)
                        {
                            var l = similar.ToList();
                            l.Sort(CompareIdsByQuality);
                            return l.Last();
                        }
                        return id;
                    }
                    scc = scc.Select(MapTo).ToHashSet().ToList();

                    // only one texture after identical textures were removed
                    if (scc.Count < 2) return;

                    var dir = Path.Join(_sccDirectory, "" + i);
                    Directory.CreateDirectory(dir);

                    var largest = scc.Select(id => DS3.OriginalSize[id].Width).Max();

                    foreach (var id in scc)
                    {
                        token.CheckCanceled();

                        var source = w.GetExtractPath(id);
                        var target = Path.Join(dir, $"{id.Category.ToString()}-{Path.GetFileNameWithoutExtension(source)}@{DS3.OriginalSize[id].Width}px.png");

                        var image = source.LoadTextureMap();
                        if (DS3.OriginalSize[id].Width < largest)
                            image = image.UpSample(largest / image.Width);

                        image.GetAlpha().SaveAsPng(target);
                    }
                });
            };
        }
        internal static Action<SubProgressToken> ReadAlphaSCCDirectory(Workspace w)
        {
            return token =>
            {
                var files = Directory.GetFiles(_sccDirectory, "*", SearchOption.AllDirectories);

                static TexId SCCFilenameToTexId(string file)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    var i = name.IndexOf('@');
                    if (i != -1) name = name.Substring(0, i);
                    name = name.Replace('-', '/');
                    return new TexId(name);
                }
                var components = files
                    .GroupBy(f => Path.GetDirectoryName(f))
                    .Select(g => g.Select(SCCFilenameToTexId).ToList())
                    .SelectMany(l => l.Select(i => (l, i)))
                    .ToDictionary(p => p.i, p => p.l);

                foreach (var (id, others) in DS3.AlphaCopiesCertain)
                {
                    var c = components.GetOrAdd(id);
                    c.AddRange(others.Except(c));
                }

                var pairs = DS3.PairsFromCopies(components);
                pairs.SaveAsJson(DataFile(@"alpha-copies-certain.json"));

                var scc = StronglyConnectedComponents.Find(DS3.OriginalSize.Keys, id =>
                {
                    var l = new List<TexId>() { id };
                    if (DS3.AlphaCopiesUncertain.TryGetValue(id, out var copies))
                    {
                        l.AddRange(copies);
                    }
                    return l;
                }).SelectMany(l => l.Select(i => (l, i))).ToDictionary(p => p.i, p => p.l);
                var sccPairs = DS3.PairsFromCopies(scc);

                var rejected = sccPairs.Except(pairs).ToList();
                rejected.SaveAsJson(DataFile(@"alpha-copies-rejected.json"));
            };
        }

        public static IReadOnlyDictionary<TexId, TexId> AlphaRepresentativeOf
            = DataFile(@"alpha-representative.json").LoadJsonFile<Dictionary<TexId, TexId>>();
        public static IReadOnlyCollection<TexId> AlphaRepresentatives = AlphaRepresentativeOf.Values.ToHashSet();
        internal static Action<SubProgressToken> CreateAlphaRepresentativeIndex(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus($"Searching for files");
                var ids = DS3.OriginalSize.Keys.ToList();
                ids.Sort();

                token.SubmitStatus($"Indexing");
                var representative = new Dictionary<TexId, TexId>();

                ConcurrentDictionary<TexId, double> artifactScoreCache = new ConcurrentDictionary<TexId, double>();
                double GetArtifactsScore(TexId id)
                {
                    if (artifactScoreCache.TryGetValue(id, out var score)) return score;
                    score = w.GetExtractPath(id).LoadTextureMap().GetAlpha().BCArtifactsScore();
                    artifactScoreCache[id] = score;
                    return score;
                }

                token.ForAllParallel(ids, id =>
                {
                    if (!AlphaCopiesCertain.TryGetValue(id, out var othersSet) || othersSet.Count == 0) return;

                    var others = othersSet.ToList();

                    // We cannot use a texture with binary alpha to represent a texture with full 8 bit alpha.
                    if (id.GetTransparency() == TransparencyKind.Full)
                    {
                        others.RemoveAll(id => id.GetTransparency() != TransparencyKind.Full);
                        if (others.Count == 0) return;
                    }

                    var largestWidth = others.Max(id => DS3.OriginalSize[id].Width);
                    others.RemoveAll(id => DS3.OriginalSize[id].Width < largestWidth);

                    // Don't use binary if full 8 bit are available.
                    var has8Bit = others.Any(id => id.GetTransparency() == TransparencyKind.Full);
                    if (has8Bit)
                        others.RemoveAll(id => id.GetTransparency() != TransparencyKind.Full);

                    // If there multiple 8 bit alphas, choose the one with the least BC artifacts
                    if (has8Bit && others.Count >= 2)
                    {
                        var maxScore = others.Min(GetArtifactsScore) * 1.05; // 5% tolerance
                        others.RemoveAll(id => GetArtifactsScore(id) > maxScore);
                    }

                    others.Sort(CompareIdsByQuality);
                    var r = others.Last();
                    if (r != id)
                    {
                        lock (representative)
                        {
                            representative[id] = r;
                        }
                    }
                });

                token.SubmitStatus("Saving JSON");
                representative.SaveAsJson(DataFile(@"alpha-representative.json"));
            };
        }

        public static IReadOnlyDictionary<TexId, HashSet<TexId>> NormalCopiesUncertain
            = CopiesFromPairs(DataFile(@"normal-copies-uncertain.json").LoadJsonFile<List<(TexId, TexId)>>());
        internal static Action<SubProgressToken> CreateNormalCopyIndex(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Searching for files");
                var files = Directory.GetFiles(w.ExtractDir, "*.dds", SearchOption.AllDirectories)
                    // ignore all images that are just solid colors
                    .Where(f => !TexId.FromPath(f).IsSolidColor())
                    // only normals
                    .Where(f => TexId.FromPath(f).GetTexKind() == TexKind.Normal)
                    .ToArray();

                var index = CopyIndex.Create(token.Reserve(0.5), files, r => new NormalImageHasher(r));

                var copies = new Dictionary<TexId, List<TexId>>();

                token.SubmitStatus($"Looking up {files.Length} files");
                token.ForAllParallel(files, f =>
                {
                    var id = TexId.FromPath(f);
                    var set = new HashSet<TexId>() { id };

                    try
                    {
                        var image = f.LoadTextureMap();

                        // small images suffer more from compression artifacts, so we want to given them a boost
                        var spread = image.Count <= 64 * 64 ? 8 : image.Count <= 128 * 128 ? 5 : 3;
                        var similar = index.GetSimilar(image, (byte)spread);
                        if (similar != null)
                        {
                            foreach (var e in similar)
                            {
                                var eId = TexId.FromPath(e.File);
                                set.Add(eId);
                            }
                        }
                    }
                    catch (System.Exception)
                    {
                        // ignore
                    }

                    var result = new List<TexId>(set);
                    result.Sort();

                    lock (copies)
                    {
                        copies[id] = result;
                    }
                });

                token.SubmitStatus("Saving JSON");
                PairsFromCopies(copies).SaveAsJson(DataFile(@"normal-copies-uncertain.json"));
            };
        }
        internal static Action<SubProgressToken> CreateNormalIdenticalIndex(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Finding identical");
                var identical = new Dictionary<TexId, HashSet<TexId>>();

                var cache = new ConcurrentDictionary<(TexId, TexId), bool>();
                bool AreIdentical(TexId a, TexId b)
                {
                    if (a == b) return true;
                    if (DS3.OriginalSize[a].Width != DS3.OriginalSize[b].Width) return false;

                    if (a > b) (b, a) = (a, b);
                    var key = (a, b);

                    if (cache!.TryGetValue(key, out var cachedResult)) return cachedResult;

                    var imageA = w.GetExtractPath(a).LoadTextureMap();
                    var imageB = w.GetExtractPath(b).LoadTextureMap();
                    var identical = true;
                    for (int i = 0; i < imageA.Count; i++)
                    {
                        var pa = imageA[i];
                        var pb = imageB[i];

                        var diffR = Math.Abs(imageA[i].R - imageB[i].R);
                        var diffG = Math.Abs(imageA[i].G - imageB[i].G);

                        const int MaxDiff = 2;
                        if (diffR > MaxDiff || diffG > MaxDiff)
                        {
                            identical = false;
                            break;
                        }
                    }

                    cache[key] = identical;
                    return identical;
                }

                token.ForAllParallel(NormalCopiesUncertain.Values, copies =>
                {
                    foreach (var a in copies)
                    {
                        foreach (var b in copies)
                        {
                            if (AreIdentical(a, b))
                            {
                                lock (identical)
                                {
                                    static HashSet<TexId> NewHashSet(TexId id) => new HashSet<TexId>() { id };
                                    identical.GetOrAdd(a, NewHashSet).Add(b);
                                    identical.GetOrAdd(b, NewHashSet).Add(a);
                                }
                            }
                        }
                    }
                });

                token.SubmitStatus("Saving JSON");
                PairsFromCopies(identical).SaveAsJson(DataFile(@"normal-copies-certain.json"));
                new List<(TexId, TexId)>().SaveAsJson(DataFile(@"normal-copies-rejected.json"));
            };
        }
        public static IReadOnlyDictionary<TexId, HashSet<TexId>> NormalCopiesCertain
            = CopiesFromPairs(DataFile(@"normal-copies-certain.json").LoadJsonFile<List<(TexId, TexId)>>());
        public static IReadOnlyDictionary<TexId, HashSet<TexId>> NormalCopiesRejected
            = CopiesFromPairs(DataFile(@"normal-copies-rejected.json").LoadJsonFile<List<(TexId, TexId)>>());
        internal static Action<SubProgressToken> CreateNormalSCCDirectory(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Getting SCC");
                var scc = StronglyConnectedComponents.Find(DS3.OriginalSize.Keys, id =>
                {
                    var l = new List<TexId>() { id };
                    if (DS3.NormalCopiesUncertain.TryGetValue(id, out var copies))
                    {
                        var without = DS3.NormalCopiesRejected.GetOrNew(id).SelectMany(i => DS3.NormalCopiesCertain.GetOrNew(i));
                        l.AddRange(copies.Except(without));
                    }
                    return l;
                }).Where(l => l.Count >= 2).OrderByDescending(l => l.Count).ToList();

                token.SubmitStatus("Copying files");
                token.ForAllParallel(scc.Select((x, i) => (x, i)), pair =>
                {
                    var (scc, i) = pair;

                    // Remove identical textures
                    static TexId MapTo(TexId id)
                    {
                        if (DS3.NormalCopiesCertain.TryGetValue(id, out var similar) && similar.Count > 0)
                        {
                            var l = similar.ToList();
                            l.Sort(CompareIdsByQuality);
                            return l.Last();
                        }
                        return id;
                    }
                    scc = scc.Select(MapTo).ToHashSet().ToList();

                    // only one texture after identical textures were removed
                    if (scc.Count < 2) return;

                    var dir = Path.Join(_sccDirectory, "" + i);
                    Directory.CreateDirectory(dir);

                    var largest = scc.Select(id => DS3.OriginalSize[id].Width).Max();

                    foreach (var id in scc)
                    {
                        token.CheckCanceled();

                        var source = w.GetExtractPath(id);
                        var target = Path.Join(dir, $"{id.Category.ToString()}-{Path.GetFileNameWithoutExtension(source)}@{DS3.OriginalSize[id].Width}px.png");

                        var image = source.LoadTextureMap();
                        if (DS3.OriginalSize[id].Width < largest)
                            image = image.UpSample(largest / image.Width);

                        image.Multiply(new Rgba32(255, 255, 0, 255));
                        image.SetAlpha(255);
                        image.SaveAsPng(target);
                    }
                });
            };
        }
        internal static Action<SubProgressToken> ReadNormalSCCDirectory(Workspace w)
        {
            return token =>
            {
                var files = Directory.GetFiles(_sccDirectory, "*", SearchOption.AllDirectories);

                static TexId SCCFilenameToTexId(string file)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    var i = name.IndexOf('@');
                    if (i != -1) name = name.Substring(0, i);
                    name = name.Replace('-', '/');
                    return new TexId(name);
                }
                var components = files
                    .GroupBy(f => Path.GetDirectoryName(f))
                    .Select(g => g.Select(SCCFilenameToTexId).ToList())
                    .SelectMany(l => l.Select(i => (l, i)))
                    .ToDictionary(p => p.i, p => p.l);

                foreach (var (id, others) in DS3.NormalCopiesCertain)
                {
                    var c = components.GetOrAdd(id);
                    c.AddRange(others.Except(c));
                }

                var pairs = DS3.PairsFromCopies(components);
                pairs.SaveAsJson(DataFile(@"normal-copies-certain.json"));

                var scc = StronglyConnectedComponents.Find(DS3.OriginalSize.Keys, id =>
                {
                    var l = new List<TexId>() { id };
                    if (DS3.NormalCopiesUncertain.TryGetValue(id, out var copies))
                    {
                        l.AddRange(copies);
                    }
                    return l;
                }).SelectMany(l => l.Select(i => (l, i))).ToDictionary(p => p.i, p => p.l);
                var sccPairs = DS3.PairsFromCopies(scc);

                var rejected = sccPairs.Except(pairs).ToList();
                rejected.SaveAsJson(DataFile(@"normal-copies-rejected.json"));
            };
        }

        public static IReadOnlyDictionary<TexId, TexId> NormalRepresentativeOf
            = DataFile(@"normal-representative.json").LoadJsonFile<Dictionary<TexId, TexId>>();
        public static IReadOnlyCollection<TexId> NormalRepresentatives = NormalRepresentativeOf.Values.ToHashSet();
        internal static Action<SubProgressToken> CreateNormalRepresentativeIndex()
        {
            return token =>
            {
                token.SubmitStatus($"Searching for files");
                var ids = DS3.OriginalSize.Keys.ToList();
                ids.Sort();

                token.SubmitStatus($"Indexing");
                var representative = new Dictionary<TexId, TexId>();
                token.ForAllParallel(ids, id =>
                {
                    if (!NormalCopiesCertain.TryGetValue(id, out var othersSet) || othersSet.Count == 0) return;

                    var others = othersSet.ToList();
                    others.Sort(CompareIdsByQuality);

                    var r = others.Last();
                    if (r != id)
                    {
                        lock (representative)
                        {
                            representative[id] = r;
                        }
                    }
                });

                token.SubmitStatus("Saving JSON");
                representative.SaveAsJson(DataFile(@"normal-representative.json"));
            };
        }

        public static IReadOnlyDictionary<TexId, HashSet<TexId>> GlossCopiesUncertain
            = CopiesFromPairs(DataFile(@"gloss-copies-uncertain.json").LoadJsonFile<List<(TexId, TexId)>>());
        internal static Action<SubProgressToken> CreateGlossCopyIndex(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Searching for files");
                var files = Directory.GetFiles(w.ExtractDir, "*.dds", SearchOption.AllDirectories)
                    // ignore all images that are just solid colors
                    .Where(f => !TexId.FromPath(f).IsSolidColor())
                    // only normals
                    .Where(f => TexId.FromPath(f).GetTexKind() == TexKind.Normal)
                    // ignore all gloss maps that are just solid colors
                    .Where(f => DS3.OriginalColorDiff[TexId.FromPath(f)].B > 12)
                    .ToArray();

                var index = CopyIndex.Create(token.Reserve(0.5), files, r => new BlueChannelImageHasher(r));

                var copies = new Dictionary<TexId, List<TexId>>();

                token.SubmitStatus($"Looking up {files.Length} files");
                token.ForAllParallel(files, f =>
                {
                    var id = TexId.FromPath(f);
                    var set = new HashSet<TexId>() { id };

                    try
                    {
                        var image = f.LoadTextureMap();

                        // small images suffer more from compression artifacts, so we want to given them a boost
                        var spread = image.Count <= 64 * 64 ? 10 : image.Count <= 128 * 128 ? 6 : 4;
                        var similar = index.GetSimilar(image, (byte)spread);
                        if (similar != null)
                        {
                            foreach (var e in similar)
                            {
                                var eId = TexId.FromPath(e.File);
                                set.Add(eId);
                            }
                        }
                    }
                    catch (System.Exception)
                    {
                        // ignore
                    }

                    var result = new List<TexId>(set);
                    result.Sort();

                    lock (copies)
                    {
                        copies[id] = result;
                    }
                });

                token.SubmitStatus("Saving JSON");
                PairsFromCopies(copies).SaveAsJson(DataFile(@"gloss-copies-uncertain.json"));
            };
        }
        internal static Action<SubProgressToken> CreateGlossIdenticalIndex(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Finding identical");
                var identical = new Dictionary<TexId, HashSet<TexId>>();

                var cache = new ConcurrentDictionary<(TexId, TexId), bool>();
                bool AreIdentical(TexId a, TexId b)
                {
                    if (a == b) return true;
                    if (DS3.OriginalSize[a].Width != DS3.OriginalSize[b].Width) return false;

                    if (a > b) (b, a) = (a, b);
                    var key = (a, b);

                    if (cache!.TryGetValue(key, out var cachedResult)) return cachedResult;

                    var imageA = w.GetExtractPath(a).LoadTextureMap();
                    var imageB = w.GetExtractPath(b).LoadTextureMap();
                    var identical = true;
                    for (int i = 0; i < imageA.Count; i++)
                    {
                        var pa = imageA[i];
                        var pb = imageB[i];

                        var diffB = Math.Abs(imageA[i].B - imageB[i].B);

                        const int MaxDiff = 8;
                        if (diffB > MaxDiff)
                        {
                            identical = false;
                            break;
                        }
                    }

                    cache[key] = identical;
                    return identical;
                }

                token.ForAllParallel(GlossCopiesUncertain.Values, copies =>
                {
                    foreach (var a in copies)
                    {
                        foreach (var b in copies)
                        {
                            if (AreIdentical(a, b))
                            {
                                lock (identical)
                                {
                                    static HashSet<TexId> NewHashSet(TexId id) => new HashSet<TexId>() { id };
                                    identical.GetOrAdd(a, NewHashSet).Add(b);
                                    identical.GetOrAdd(b, NewHashSet).Add(a);
                                }
                            }
                        }
                    }
                });

                identical = StronglyConnectedComponents.Find(DS3.OriginalSize.Keys, id =>
                {
                    return new List<TexId>(identical.GetOrNew(id)) { id };
                }).Select(l => l.ToHashSet()).SelectMany(l => l.Select(i => (l, i))).ToDictionary(p => p.i, p => p.l);

                token.SubmitStatus("Saving JSON");
                PairsFromCopies(identical).SaveAsJson(DataFile(@"gloss-copies-certain.json"));
                new List<(TexId, TexId)>().SaveAsJson(DataFile(@"gloss-copies-rejected.json"));
            };
        }
        public static IReadOnlyDictionary<TexId, HashSet<TexId>> GlossCopiesCertain
            = CopiesFromPairs(DataFile(@"gloss-copies-certain.json").LoadJsonFile<List<(TexId, TexId)>>());
        public static IReadOnlyDictionary<TexId, HashSet<TexId>> GlossCopiesRejected
            = CopiesFromPairs(DataFile(@"gloss-copies-rejected.json").LoadJsonFile<List<(TexId, TexId)>>());
        internal static Action<SubProgressToken> CreateGlossSCCDirectory(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Getting SCC");
                var scc = StronglyConnectedComponents.Find(DS3.OriginalSize.Keys, id =>
                {
                    var l = new List<TexId>() { id };
                    if (DS3.GlossCopiesUncertain.TryGetValue(id, out var copies))
                    {
                        var without = DS3.GlossCopiesRejected.GetOrNew(id).SelectMany(i => DS3.GlossCopiesCertain.GetOrNew(i));
                        l.AddRange(copies.Except(without));
                    }
                    return l;
                }).Where(l => l.Count >= 2).OrderByDescending(l => l.Count).ToList();

                token.SubmitStatus("Copying files");
                token.ForAllParallel(scc.Select((x, i) => (x, i)), pair =>
                {
                    var (scc, i) = pair;

                    // Remove identical textures
                    static TexId MapTo(TexId id)
                    {
                        if (DS3.GlossCopiesCertain.TryGetValue(id, out var similar) && similar.Count > 0)
                        {
                            var l = similar.ToList();
                            l.Sort(CompareIdsByQuality);
                            return l.Last();
                        }
                        return id;
                    }
                    scc = scc.Select(MapTo).ToHashSet().ToList();

                    // only one texture after identical textures were removed
                    if (scc.Count < 2) return;

                    var dir = Path.Join(_sccDirectory, "" + i);
                    Directory.CreateDirectory(dir);

                    var largest = scc.Select(id => DS3.OriginalSize[id].Width).Max();

                    foreach (var id in scc)
                    {
                        token.CheckCanceled();

                        var source = w.GetExtractPath(id);
                        var target = Path.Join(dir, $"{id.Category.ToString()}-{Path.GetFileNameWithoutExtension(source)}@{DS3.OriginalSize[id].Width}px.png");

                        var image = source.LoadTextureMap();
                        if (DS3.OriginalSize[id].Width < largest)
                            image = image.UpSample(largest / image.Width);

                        image.Multiply(new Rgba32(0, 0, 255, 255));
                        image.SetAlpha(255);
                        image.SaveAsPng(target);
                    }
                });
            };
        }
        internal static Action<SubProgressToken> ReadGlossSCCDirectory(Workspace w)
        {
            return token =>
            {
                var files = Directory.GetFiles(_sccDirectory, "*", SearchOption.AllDirectories);

                static TexId SCCFilenameToTexId(string file)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    var i = name.IndexOf('@');
                    if (i != -1) name = name.Substring(0, i);
                    name = name.Replace('-', '/');
                    return new TexId(name);
                }
                var components = files
                    .GroupBy(f => Path.GetDirectoryName(f))
                    .Select(g => g.Select(SCCFilenameToTexId).ToList())
                    .SelectMany(l => l.Select(i => (l, i)))
                    .ToDictionary(p => p.i, p => p.l);

                foreach (var (id, others) in DS3.GlossCopiesCertain)
                {
                    var c = components.GetOrAdd(id);
                    c.AddRange(others.Except(c));
                }

                var pairs = DS3.PairsFromCopies(components);
                pairs.SaveAsJson(DataFile(@"gloss-copies-certain.json"));

                var scc = StronglyConnectedComponents.Find(DS3.OriginalSize.Keys, id =>
                {
                    var l = new List<TexId>() { id };
                    if (DS3.GlossCopiesUncertain.TryGetValue(id, out var copies))
                    {
                        l.AddRange(copies);
                    }
                    return l;
                }).SelectMany(l => l.Select(i => (l, i))).ToDictionary(p => p.i, p => p.l);
                var sccPairs = DS3.PairsFromCopies(scc);

                var rejected = sccPairs.Except(pairs).ToList();
                rejected.SaveAsJson(DataFile(@"gloss-copies-rejected.json"));
            };
        }

        public static IReadOnlyDictionary<TexId, TexId> GlossRepresentativeOf
            = DataFile(@"gloss-representative.json").LoadJsonFile<Dictionary<TexId, TexId>>();
        public static IReadOnlyCollection<TexId> GlossRepresentatives = GlossRepresentativeOf.Values.ToHashSet();
        internal static Action<SubProgressToken> CreateGlossRepresentativeIndex()
        {
            return token =>
            {
                token.SubmitStatus($"Searching for files");
                var ids = DS3.OriginalSize.Keys.ToList();
                ids.Sort();

                token.SubmitStatus($"Indexing");
                var representative = new Dictionary<TexId, TexId>();
                token.ForAllParallel(ids, id =>
                {
                    if (!GlossCopiesCertain.TryGetValue(id, out var othersSet) || othersSet.Count == 0) return;

                    var others = othersSet.ToList();
                    others.Sort(CompareIdsByQuality);

                    var r = others.Last();
                    if (r != id)
                    {
                        lock (representative)
                        {
                            representative[id] = r;
                        }
                    }
                });

                token.SubmitStatus("Saving JSON");
                representative.SaveAsJson(DataFile(@"gloss-representative.json"));
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
                        a = a.GetRepresentative();
                        if (!n.IsSolidColor() && !a.IsSolidColor())
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

    public readonly struct ColorCode6x6 : IEquatable<ColorCode6x6>
    {
        public static readonly uint Max = 46656;
        private static readonly Rgba32[] _colors = new Rgba32[] {
            new Rgba32(255, 0, 0, 255),
            new Rgba32(255, 255, 0, 255),
            new Rgba32(0, 255, 0, 255),
            new Rgba32(0, 255, 255, 255),
            new Rgba32(0, 0, 255, 255),
            new Rgba32(255, 0, 255, 255),
        };
        private static readonly char[] _letters = new[] { 'R', 'Y', 'G', 'C', 'B', 'M' };

        public uint Number { get; }

        public ColorCode6x6(uint n)
        {
            n %= Max;
            Number = Math.Min(Reverse(n), n);
        }
        private static void GetCodes(uint n, out uint c0, out uint c1, out uint c2, out uint c3, out uint c4, out uint c5)
        {
            c0 = n % 6;
            c1 = (n /= 6) % 6;
            c2 = (n /= 6) % 6;
            c3 = (n /= 6) % 6;
            c4 = (n /= 6) % 6;
            c5 = n / 6;
        }
        private static uint Reverse(uint n)
        {
            GetCodes(n, out var c0, out var c1, out var c2, out var c3, out var c4, out var c5);
            return c0 * 7776 + c1 * 1296 + c2 * 216 + c3 * 36 + c4 * 6 + c5;
        }

        public Rgba32[] GetColors()
        {
            GetCodes(Number, out var c0, out var c1, out var c2, out var c3, out var c4, out var c5);
            return new Rgba32[] { _colors[c0], _colors[c1], _colors[c2], _colors[c3], _colors[c4], _colors[c5] };
        }

        public override bool Equals(object? obj) => obj is ColorCode6x6 other ? Equals(other) : false;
        public bool Equals(ColorCode6x6 other) => Number == other.Number;
        public override int GetHashCode() => Number.GetHashCode();

        public override string ToString()
        {
            GetCodes(Number, out var c0, out var c1, out var c2, out var c3, out var c4, out var c5);
            return $"{_letters[c0]}{_letters[c1]}{_letters[c2]}{_letters[c3]}{_letters[c4]}{_letters[c5]}";
        }
        public static ColorCode6x6 Parse(string input)
        {
            if (input.Length != 6) throw new Exception("Expected the string to be 6 letters long.");

            static uint ToCode(char c)
            {
                switch (c)
                {
                    case 'r':
                    case 'R':
                        return 0;
                    case 'y':
                    case 'Y':
                        return 1;
                    case 'g':
                    case 'G':
                        return 2;
                    case 'c':
                    case 'C':
                        return 3;
                    case 'b':
                    case 'B':
                        return 4;
                    case 'm':
                    case 'M':
                        return 5;
                    default:
                        throw new Exception($"Invalid color letter {c}. Expected RGBYCM.");
                }
            }

            var c0 = ToCode(input[0]);
            var c1 = ToCode(input[1]);
            var c2 = ToCode(input[2]);
            var c3 = ToCode(input[3]);
            var c4 = ToCode(input[4]);
            var c5 = ToCode(input[5]);

            var n = c0 + c1 * 6 + c2 * 36 + c3 * 216 + c4 * 1296 + c5 * 7776;
            return new ColorCode6x6(n);
        }

        public static bool operator ==(ColorCode6x6 rhs, ColorCode6x6 lhs) => rhs.Equals(lhs);
        public static bool operator !=(ColorCode6x6 rhs, ColorCode6x6 lhs) => !(rhs == lhs);
    }
}
