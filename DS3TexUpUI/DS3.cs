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
using System.Numerics;

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

        private static string DataDir() => Path.Join(AppDomain.CurrentDomain.BaseDirectory, "data");
        internal static string DataFile(string name) => Path.Join(DataDir(), name);

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
                        var kind = TextureTypeToTexKind.GetOrDefault(tex.Type, TexKind.Unknown);
                        if (kind != TexKind.Unknown)
                        {
                            foreach (var id in TexId.FromTexturePath(tex, info.FlverPath))
                            {
                                yield return (id, kind);
                            }
                        }
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

        public static IReadOnlyDictionary<string, HashSet<TexId>> Homographs
            = DataFile(@"homographs.json").LoadJsonFile<Dictionary<string, HashSet<TexId>>>();
        internal static Action<SubProgressToken> CreateHomographIndex()
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

        public static UpscaleFactor OutputUpscale = UpscaleFactor.LoadFromDir(DataDir());

        public class UpscaleFactor
        {
            public HashSet<TexId> Ignore { get; set; } = new HashSet<TexId>();
            public Dictionary<string, int> UpscaleChr { get; set; } = new Dictionary<string, int>();
            public Dictionary<TexId, int> Upscale { get; set; } = new Dictionary<TexId, int>();

            public int this[TexId id]
            {
                get
                {
                    int upscale;

                    // per-texture overwrites
                    if (Upscale.TryGetValue(id, out upscale))
                        return upscale;

                    if (id.Category.Equals("chr", StringComparison.OrdinalIgnoreCase))
                    {
                        var cId = id.Name.Slice(0, 5).ToString();

                        // per-character overwrites
                        if (UpscaleChr.TryGetValue(cId, out upscale))
                            return upscale;
                    }

                    // 2x for maps, 4x for everything else
                    return id.Category.StartsWith("m") ? 2 : 4;
                }
            }

            public static UpscaleFactor LoadFromDir(string dir)
            {
                return new UpscaleFactor()
                {
                    Ignore = Path.Join(dir, @"output-ignore.json").LoadJsonFile<HashSet<TexId>>(),
                    UpscaleChr = Path.Join(dir, @"output-upscale-chr.json").LoadJsonFile<Dictionary<string, int>>(),
                    Upscale = Path.Join(dir, @"output-upscale.json").LoadJsonFile<Dictionary<TexId, int>>(),
                };
            }
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

        public static EquivalenceCollection<TexId> CopiesCertain
            = DataFile(@"copies-certain.json").LoadJsonFile<EquivalenceCollection<TexId>>();
        internal static UncertainCopies _generalCopiesConfig = new UncertainCopies()
        {
            CertainFile = @"copies-certain.json",
            UncertainFile = @"copies-uncertain.json",
            RejectedFile = @"copies-rejected.json",
            CopyFilter = id =>
            {
                // ignore all images that are just solid colors
                if (id.IsSolidColor()) return false;

                return true;
            },
            CopySpread = image => image.Count <= 64 * 64 ? 12 : image.Count <= 128 * 128 ? 8 : 5,
            MaxDiff = new Rgba32(2, 2, 2, 2),
        };

        public static IReadOnlyDictionary<TexId, TexId> RepresentativeOf
            = DataFile(@"representative.json").LoadJsonFile<Dictionary<TexId, TexId>>();
        public static IReadOnlyCollection<TexId> Representatives = RepresentativeOf.Values.ToHashSet();
        internal static Action<SubProgressToken> CreateRepresentativeIndex()
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

        public static EquivalenceCollection<TexId> AlphaCopiesCertain
            = DataFile(@"alpha-copies-certain.json").LoadJsonFile<EquivalenceCollection<TexId>>();
        internal static UncertainCopies _alphaCopiesConfig = new UncertainCopies()
        {
            CertainFile = @"alpha-copies-certain.json",
            UncertainFile = @"alpha-copies-uncertain.json",
            RejectedFile = @"alpha-copies-rejected.json",
            CopyFilter = id =>
            {
                // ignore all images that are just solid colors
                if (id.IsSolidColor()) return false;
                // ignore all images that aren't transparanet
                var t = id.GetTransparency();
                if (t != TransparencyKind.Binary && t != TransparencyKind.Full) return false;

                return true;
            },
            CopyHasherFactory = r => new AlphaImageHasher(r),
            CopySpread = image => image.Count <= 64 * 64 ? 12 : 8,
            MaxDiff = new Rgba32(255, 255, 255, 2),
            ModifyImage = image =>
            {
                foreach (ref var p in image.Data.AsSpan())
                {
                    p.R = p.A;
                    p.G = p.A;
                    p.B = p.A;
                    p.A = 255;
                }
            },
        };

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

        public static EquivalenceCollection<TexId> NormalCopiesCertain
            = DataFile(@"normal-copies-certain.json").LoadJsonFile<EquivalenceCollection<TexId>>();
        internal static UncertainCopies _normalCopiesConfig = new UncertainCopies()
        {
            CertainFile = @"normal-copies-certain.json",
            UncertainFile = @"normal-copies-uncertain.json",
            RejectedFile = @"normal-copies-rejected.json",
            CopyFilter = id =>
            {
                // ignore all images that are just solid colors
                if (id.IsSolidColor()) return false;
                // only normals
                if (id.GetTexKind() != TexKind.Normal) return false;

                return true;
            },
            CopyHasherFactory = r => new NormalImageHasher(r),
            CopySpread = image => image.Count <= 64 * 64 ? 8 : image.Count <= 128 * 128 ? 5 : 3,
            MaxDiff = new Rgba32(2, 2, 255, 255),
            ModifyImage = image =>
            {
                image.Multiply(new Rgba32(255, 255, 0, 0));
                image.SetAlpha(255);
            },
        };

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

        public static EquivalenceCollection<TexId> GlossCopiesCertain
            = DataFile(@"gloss-copies-certain.json").LoadJsonFile<EquivalenceCollection<TexId>>();
        internal static UncertainCopies _glossCopiesConfig = new UncertainCopies()
        {
            CertainFile = @"gloss-copies-certain.json",
            UncertainFile = @"gloss-copies-uncertain.json",
            RejectedFile = @"gloss-copies-rejected.json",
            CopyFilter = id =>
            {
                // ignore all images that are just solid colors
                if (id.IsSolidColor()) return false;
                // only normals
                if (id.GetTexKind() != TexKind.Normal) return false;
                // ignore all gloss maps that are just solid colors
                if (DS3.OriginalColorDiff[id].B <= 12) return false;

                return true;
            },
            CopyHasherFactory = r => new BlueChannelImageHasher(r),
            CopySpread = image => image.Count <= 64 * 64 ? 10 : image.Count <= 128 * 128 ? 6 : 4,
            MaxDiff = new Rgba32(255, 255, 8, 255),
            ModifyImage = image =>
            {
                foreach (ref var p in image.Data.AsSpan())
                {
                    p.R = p.B;
                    p.G = p.B;
                    p.A = 255;
                }
            },
        };

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
                            foreach (var id in TexId.FromTexturePath(tex, info.FlverPath))
                            {
                                usage.GetOrAdd(id).GetOrAdd(info.FlverPath).Add(mat.Name);
                            }
                        }
                    }
                }

                token.SubmitStatus("Saving copies JSON");
                usage.SaveAsJson(DataFile(@"usage.json"));
            };
        }

        public static IReadOnlyCollection<TexId> Unused
            = DataFile(@"unused.json").LoadJsonFile<HashSet<TexId>>();
        internal static Action<SubProgressToken> CreateUnused()
        {
            return token =>
            {
                token.SubmitStatus($"Searching for files");
                var unused = DS3.OriginalSize.Keys.Where(id => !UsedBy.ContainsKey(id)).ToList();
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
                void AddToIndex((IReadOnlyCollection<TexId> ids, Vector2 scale) normal, (IReadOnlyCollection<TexId> ids, Vector2 scale) albedo)
                {
                    if (normal.scale != albedo.scale)
                    {
                        return;
                    }

                    foreach (var a in albedo.ids.Select(id => id.GetRepresentative()))
                    {
                        foreach (var n in normal.ids)
                        {
                            if (!n.IsSolidColor() && !a.IsSolidColor())
                            {
                                if (DS3.OriginalSize.TryGetValue(n, out var nSize) && DS3.OriginalSize.TryGetValue(a, out var aSize))
                                {
                                    if (SizeRatio.Of(nSize) == SizeRatio.Of(aSize))
                                    {
                                        index.GetOrAdd(n).Add(a);
                                    }
                                }
                            }
                        }
                    }
                };

                token.SubmitStatus($"Analysing flver files");
                foreach (var info in DS3.ReadAllFlverMaterialInfo())
                {
                    var a = new List<((IReadOnlyCollection<TexId>, Vector2) id, string type)>();
                    var n = new List<((IReadOnlyCollection<TexId>, Vector2) id, string type)>();
                    foreach (var mat in info.Materials)
                    {
                        a.Clear();
                        n.Clear();
                        foreach (var tex in mat.Textures)
                        {
                            var i = TexId.FromTexturePath(tex, info.FlverPath);
                            var kind = DS3.TextureTypeToTexKind.GetOrDefault(tex.Type, TexKind.Unknown);
                            if (kind == TexKind.Albedo)
                                a.Add(((i, tex.Scale), tex.Type));
                            else if (kind == TexKind.Normal)
                                n.Add(((i, tex.Scale), tex.Type));
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
                        AddToIndex((id.GetHomographs(), Vector2.One), (GetAlbedoByName(id).GetHomographs(), Vector2.One));
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

                        if (certainPercentage >= 0.60)
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
        internal static Action<IProgressToken> GenerateFlverTextureInfoFiles()
        {
            return token =>
            {
                var ignoreFiles = new HashSet<string>() {
                    "dummy128",
                    "BurningBlendMask",
                    "SYSTEX_DummyAlbedo",
                    "SYSTEX_DummyShininess",
                    "SYSTEX_DummySpecular",
                    "SYSTEX_DummyNormal",
                    "SYSTEX_DummyEmissive",
                    "SYSTEX_DummyDamagedNormal",
                    "SYSTEX_DummyScatteringMask",
                    "SYSTEX_DummyBurn_em",
                    "SYSTEX_DummyBurn_m",

                    // weird, I know
                    "m30_00_base_a",
                    "m30_00_base_n",
                    "m30_00_base_r",
                    "m30_00_base_s",
                    "m30_00_base2_a",
                    "m30_00_base2_n",
                    "m30_00_base2_r",
                    "m30_00_base2_s",
                    "m30_ground_06_s",
                    "m30_ground_08_s",
                };

                token.SubmitStatus("Converting files");
                token.ForAllParallel(Directory.GetFiles(DataFile(@"materials"), "*.json"), file =>
                {
                    var items = file.LoadJsonFile<List<FlverMaterialInfo>>().Select(info =>
                    {
                        var result = new FlverTextureInfo() { FlverPath = info.FlverPath };
                        result.Materials.AddRange(info.Materials.Select(mat =>
                        {
                            var result = new FlverTextureInfo.Material()
                            {
                                Name = mat.Name,
                                MTD = mat.MTD,
                            };
                            foreach (var tex in mat.Textures)
                            {
                                if (tex.Type == "g_DOLTexture1" || tex.Type == "g_DOLTexture2") continue;
                                if (tex.Path.Contains("Other\\SysTex")) continue;
                                if (ignoreFiles.Contains(Path.GetFileNameWithoutExtension(tex.Path))) continue;

                                var id = TexId.FromTexture(tex, info.FlverPath);
                                if (id == null)
                                {
                                    var ids = TexId.FromTexturePath(tex, info.FlverPath);
                                    if (ids.Count == 1) id = ids.First();
                                }

                                if (id != null)
                                {
                                    if (!DS3.OriginalSize.ContainsKey(id.Value)) continue;
                                    if (id.Value.IsSolidColor()) continue;

                                    result.Textures[tex.Type] = id.Value.ToString();
                                }
                                else
                                {
                                    result.Textures[tex.Type] = tex.Path;
                                }
                            }

                            return result;
                        }));
                        return result;
                    }).ToList();

                    var targetDir = DataFile(@"textures");
                    Directory.CreateDirectory(targetDir);
                    items.SaveAsJson(Path.Join(targetDir, Path.GetFileName(file)));
                });
            };
        }
        private class FlverTextureInfo
        {
            public string? FlverPath { get; set; }
            public List<Material> Materials { get; } = new List<Material>();

            public class Material
            {
                public string? Name { get; set; }
                public string? MTD { get; set; }
                public Dictionary<string, string> Textures { get; set; } = new Dictionary<string, string>();
            }

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

    class UncertainCopies
    {
        public class DataFile
        {
            public string Name { get; }

            public string Read => DS3.DataFile(Name);
            public string Write => Path.Join("data", Name);

            public DataFile(string name) { Name = name; }

            public static implicit operator DataFile(string value) => new DataFile(value);
        }

        public DataFile CertainFile { get; set; } = "";
        public DataFile UncertainFile { get; set; } = "";
        public DataFile RejectedFile { get; set; } = "";

        public bool SameKind { get; set; } = false;
        public Func<SizeRatio, IImageHasher>? CopyHasherFactory { get; set; } = null;
        public Predicate<TexId> CopyFilter { get; set; } = id => true;
        public Func<ArrayTextureMap<Rgba32>, int> CopySpread { get; set; } = image => 2;
        public Rgba32 MaxDiff { get; set; } = default;
        public Action<ArrayTextureMap<Rgba32>>? ModifyImage { get; set; } = null;

        private static string GetFileName(TexId id)
        {
            var atSize = DS3.OriginalSize.TryGetValue(id, out var size) ? $"@{size.Width}px" : "";
            return $"{id.Category.ToString()}-{id.Name.ToString()}{atSize}.png";
        }
        private static TexId ParseFileName(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var i = name.IndexOf('@');
            if (i != -1) name = name.Substring(0, i);
            name = name.Replace('-', '/');
            return new TexId(name);
        }

        private static string GetUncertainDir(Workspace w) => Path.Join(w.TextureDir, "uncertain");

        private EquivalenceCollection<TexId> LoadCertain()
        {
            return CertainFile.Read.LoadJsonFile<EquivalenceCollection<TexId>>();
        }
        private EquivalenceCollection<TexId> LoadUncertain()
        {
            return UncertainFile.Read.LoadJsonFile<EquivalenceCollection<TexId>>();
        }
        private DifferenceCollection<TexId> LoadRejected()
        {
            if (!File.Exists(RejectedFile.Read)) return new DifferenceCollection<TexId>();
            return RejectedFile.Read.LoadJsonFile<DifferenceCollection<TexId>>();
        }

        public Action<SubProgressToken> CreateCopyIndex(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Searching for files");
                var files = Directory.GetFiles(w.ExtractDir, "*.dds", SearchOption.AllDirectories)
                    .Where(f => CopyFilter(TexId.FromPath(f)))
                    .ToArray();

                var index = CopyIndex.Create(token.Reserve(0.5), files, CopyHasherFactory);

                var copies = new EquivalenceCollection<TexId>();

                token.SubmitStatus($"Looking up files");
                token.ForAllParallel(files, f =>
                {
                    var id = TexId.FromPath(f);
                    var set = new HashSet<TexId>() { id };

                    try
                    {
                        var image = f.LoadTextureMap();
                        var similar = index.GetSimilar(image, (byte)CopySpread(image));
                        if (similar != null)
                        {
                            foreach (var e in similar)
                            {
                                var eId = TexId.FromPath(e.File);
                                set.Add(eId);
                            }
                        }
                    }
                    catch (System.Exception e)
                    {
                        lock (token.Lock)
                        {
                            token.SubmitLog($"Ignoring {f} due to error");
                            token.LogException(e);
                        }
                    }

                    if (SameKind)
                    {
                        var kind = id.GetTexKind();
                        set.RemoveWhere(i => i.GetTexKind() != kind);
                    }

                    lock (copies)
                    {
                        copies.Set(set);
                    }
                });

                token.SubmitStatus("Saving JSON");
                copies.SaveAsJson(UncertainFile.Write);
            };
        }

        public Action<SubProgressToken> CreateIdentical(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Finding identical");
                var identical = new EquivalenceCollection<TexId>();

                bool AreIdentical(TexId a, TexId b)
                {
                    if (a == b) return true;
                    if (DS3.OriginalSize[a].Width != DS3.OriginalSize[b].Width) return false;

                    var imageA = w.GetExtractPath(a).LoadTextureMap();
                    var imageB = w.GetExtractPath(b).LoadTextureMap();
                    var maxDiff = MaxDiff;
                    for (int i = 0; i < imageA.Count; i++)
                    {
                        var pa = imageA[i];
                        var pb = imageB[i];

                        var diffR = Math.Abs(imageA[i].R - imageB[i].R);
                        var diffG = Math.Abs(imageA[i].G - imageB[i].G);
                        var diffB = Math.Abs(imageA[i].B - imageB[i].B);
                        var diffA = Math.Abs(imageA[i].A - imageB[i].A);

                        if (diffR > maxDiff.R || diffG > maxDiff.G || diffB > maxDiff.B || diffA > maxDiff.A)
                            return false;
                    }
                    return true;
                }

                token.ForAllParallel(LoadUncertain().Classes, copies =>
                {
                    var array = copies.ToArray();

                    for (int i = 0; i < array.Length; i++)
                    {
                        for (int j = i + 1; j < array.Length; j++)
                        {
                            var a = array[i];
                            var b = array[j];

                            if (AreIdentical(a, b))
                            {
                                lock (identical)
                                {
                                    identical.Set(a, b);
                                }
                            }
                        }
                    }
                });

                token.SubmitStatus("Saving JSON");
                identical.SaveAsJson(CertainFile.Write);
            };
        }

        public Action<SubProgressToken> CreateUncertainDirectory(Workspace w)
        {
            return token =>
            {
                var uncertainDir = GetUncertainDir(w);

                EquivalenceCollection<TexId> certain = LoadCertain();
                var uncertain = LoadUncertain();
                var rejected = LoadRejected();

                token.SubmitStatus("Getting uncertain");
                var classes = EquivalenceCollection<TexId>.FromMapping(DS3.OriginalSize.Keys, id =>
                {
                    var equal = uncertain.Get(id);
                    static HashSet<TexId> NewHashSet() => new HashSet<TexId>();
                    var different = rejected.GetOrDefault(id, NewHashSet).SelectMany(certain.Get);
                    return equal.Except(different);
                }).Classes.OrderByDescending(l => l.Count).ToList();

                TexId SelectRepresentative(TexId id)
                {
                    var ids = certain.Get(id).ToList();
                    if (ids.Count == 1) return id;
                    ids.Sort();
                    return ids.OrderByDescending(id => DS3.OriginalSize[id].Width).First();
                }

                token.SubmitStatus("Copying files");
                token.ForAllParallel(classes.Select((x, i) => (x, i)), pair =>
                {
                    var (eqClass, i) = pair;

                    // Remove identical textures
                    eqClass = eqClass.Select(SelectRepresentative).ToHashSet().ToList();

                    // only one texture after identical textures were removed
                    if (eqClass.Count < 2) return;

                    var dir = Path.Join(uncertainDir, $"{i}");
                    Directory.CreateDirectory(dir);

                    var largest = eqClass.Select(id => DS3.OriginalSize[id].Width).Max();

                    foreach (var id in eqClass)
                    {
                        token.CheckCanceled();

                        var source = w.GetExtractPath(id);
                        var target = Path.Join(dir, GetFileName(id));
                        var width = DS3.OriginalSize[id].Width;

                        if (ModifyImage == null && width == largest)
                        {
                            File.Copy(source, Path.ChangeExtension(target, "dds"));
                        }
                        else
                        {
                            var image = source.LoadTextureMap();
                            ModifyImage?.Invoke(image);
                            if (width < largest) image = image.UpSample(largest / image.Width);
                            image.SaveAsPng(target);
                        }
                    }
                });
            };
        }
        public Action<SubProgressToken> ReadUncertainDirectory(Workspace w)
        {
            return token =>
            {
                var uncertainDir = Path.Join(w.TextureDir, "uncertain");

                var files = Directory.GetFiles(uncertainDir, "*", SearchOption.AllDirectories);
                var newCertain = EquivalenceCollection<TexId>.FromGroups(files, Path.GetDirectoryName, ParseFileName);
                newCertain.Set(LoadCertain());
                newCertain.SaveAsJson(CertainFile.Write);

                var rejected = DifferenceCollection<TexId>.FromUncertain(LoadUncertain(), newCertain);
                rejected.SaveAsJson(RejectedFile.Write);
            };
        }

        public Action<SubProgressToken> UpdateRejected()
        {
            return token =>
            {
                var rejected = DifferenceCollection<TexId>.FromUncertain(LoadUncertain(), LoadCertain());
                rejected.SaveAsJson(RejectedFile.Write);
            };
        }

        public Action<SubProgressToken> ManuallyMakeEqual(EquivalenceCollection<TexId> certain)
        {
            return token =>
            {
                certain.Set(LoadCertain());
                certain.SaveAsJson(CertainFile.Write);

                var rejected = DifferenceCollection<TexId>.FromUncertain(LoadUncertain(), certain);
                rejected.SaveAsJson(RejectedFile.Write);
            };
        }
    }
}
