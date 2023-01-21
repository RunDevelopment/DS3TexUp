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

        public static readonly string[] MapPieces = new string[]{
            "m30_00", // High Wall of Lothric, Consumed King's Garden
            "m30_01", // Lothric Castle
            "m31_00", // Undead Settlement
            "m32_00", // Archdragon Peak
            "m33_00", // Road of Sacrifices, Farron Keep
            "m34_01", // Grand Archives
            "m35_00", // Cathedral of the Deep
            "m37_00", // Irithyll of the Boreal Valley, Anor Londo
            "m38_00", // Catacombs of Carthus, Smouldering Lake
            "m39_00", // Irithyll Dungeon, Profaned Capital
            "m40_00", // Cemetary of Ash, Firelink Shrine, and Untended Graves
            "m41_00", // Kiln of the First Flame, Flameless Shrine
            "m45_00", // Painted World of Ariandel
            "m46_00", // Arena - Grand Roof
            "m47_00", // Arena - Kiln of Flame
            "m50_00", // Dreg Heap
            "m51_00", // The Ringed City
            "m51_01", // Filianore's Rest
            "m53_00", // Arena - Dragon Ruins
            "m54_00", // Arena - Round Plaza
        };

        public static readonly IReadOnlyList<string> Parts
            = Data.File(@"parts.json").LoadJsonFile<string[]>();

        public static IReadOnlyDictionary<string, TexKind> TextureTypeToTexKind
            = Data.File(@"texture-type-to-tex-kind.json").LoadJsonFile<Dictionary<string, TexKind>>();

        public static readonly IReadOnlyCollection<TexId> GroundTextures
            = Data.File(@"ground.json").LoadJsonFile<HashSet<TexId>>();
        public static readonly IReadOnlyCollection<TexId> GroundWithMossTextures
            = Data.File(@"ground-with-moss.json").LoadJsonFile<HashSet<TexId>>();

        public static readonly IReadOnlyCollection<TexId> SolidColor
            = Data.File(@"solid-color.json").LoadJsonFile<HashSet<TexId>>();

        public static readonly IReadOnlyDictionary<TexId, TexKind> KnownTexKinds
            = Data.File(@"tex-kinds.json").LoadJsonFile<Dictionary<TexId, TexKind>>();
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
                result.SaveAsJson(Data.File(@"tex-kinds.json", Data.Source.Local));
            };
        }

        public static IReadOnlyDictionary<TexId, TransparencyKind> Transparency
            = Data.File(@"alpha.json").LoadJsonFile<Dictionary<TexId, TransparencyKind>>();
        internal static Action<SubProgressToken> CreateTransparencyIndex(Workspace w)
            => CreateExtractedFilesIndexJson(w, Data.File(@"alpha.json", Data.Source.Local), f => DDSImage.Load(f).GetTransparency());

        public static IReadOnlyDictionary<TexId, RgbaDiff> OriginalColorDiff
            = Data.File(@"original-color-diff.json").LoadJsonFile<Dictionary<TexId, RgbaDiff>>();
        internal static Action<SubProgressToken> CreateOriginalColorDiffIndex(Workspace w)
        {
            return CreateExtractedFilesIndexJson(w, Data.File(@"original-color-diff.json", Data.Source.Local), f =>
            {
                using var image = DDSImage.Load(f);
                return image.GetMaxAbsDiff();
            });
        }

        public static IReadOnlyDictionary<TexId, Size> OriginalSize
            = Data.File(@"original-size.json").LoadJsonFile<Dictionary<TexId, Size>>();
        internal static Action<SubProgressToken> CreateOriginalSizeIndex(Workspace w)
        {
            return CreateExtractedFilesIndexJson(w, Data.File(@"original-size.json", Data.Source.Local), f =>
            {
                var (header, _) = f.ReadDdsHeader();
                return new Size((int)header.Width, (int)header.Height);
            });
        }

        public static IReadOnlyDictionary<TexId, DDSFormat> OriginalFormat
            = Data.File(@"original-format.json").LoadJsonFile<Dictionary<TexId, DDSFormat>>();
        internal static Action<SubProgressToken> CreateOriginalFormatIndex(Workspace w)
            => CreateExtractedFilesIndexJson(w, Data.File(@"original-format.json", Data.Source.Local), f => f.ReadDdsHeader().GetFormat());
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
                list.SaveAsJson(Data.File(@"format-by-tex-kind.json", Data.Source.Local));
            };
        }

        public static IReadOnlyDictionary<TexId, string> GamePath
            = Data.File(@"game-path.json").LoadJsonFile<Dictionary<TexId, string>>();
        internal static Action<SubProgressToken> CreateGamePathIndex(Workspace w)
        {
            return token =>
            {
                var index = w.GetTexFiles(token).ToDictionary(f => f.Id, f => Path.GetRelativePath(w.GameDir, f.GamePath));

                token.SubmitStatus("Saving JSON");
                index.SaveAsJson(Data.File(@"game-path.json", Data.Source.Local));
            };
        }

        public static IReadOnlyDictionary<string, HashSet<TexId>> Homographs
            = Data.File(@"homographs.json").LoadJsonFile<Dictionary<string, HashSet<TexId>>>();
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
                homographs.SaveAsJson(Data.File(@"homographs.json", Data.Source.Local));
            };
        }

        public static IReadOnlyDictionary<TexId, DDSFormat> OutputFormat
            = Data.File(@"output-format.json").LoadJsonFile<Dictionary<TexId, DDSFormat>>();
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
                new Dictionary<TexId, DDSFormat>(e).SaveAsJson(Data.File(@"output-format.json", Data.Source.Local));
            };
        }

        public static UpscaleFactor OutputUpscale = UpscaleFactor.LoadFromDir(Data.Dir());

        public class UpscaleFactor
        {
            public HashSet<TexId> Ignore { get; set; } = new HashSet<TexId>();
            public Dictionary<string, int> UpscaleChr { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, int> UpscaleObj { get; set; } = new Dictionary<string, int>();
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

                    if (id.Category.Equals("obj", StringComparison.OrdinalIgnoreCase))
                    {
                        var oId = id.Name.Slice(0, 7).ToString();

                        // per-object overwrites
                        if (UpscaleObj.TryGetValue(oId, out upscale))
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
                    UpscaleObj = Path.Join(dir, @"output-upscale-obj.json").LoadJsonFile<Dictionary<string, int>>(),
                    Upscale = Path.Join(dir, @"output-upscale.json").LoadJsonFile<Dictionary<TexId, int>>(),
                };
            }
        }

        public static IReadOnlyDictionary<TexId, ColorCode6x6> ColorCode
            = Data.File(@"color-code.json").LoadJsonFile<Dictionary<TexId, ColorCode6x6>>();
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
                index.SaveAsJson(Data.File(@"color-code.json"));
            };
        }

        public static EquivalenceCollection<TexId> CopiesCertain
            = Data.File(@"copies-certain.json").LoadJsonFile<EquivalenceCollection<TexId>>();
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
            MaxEqClassSize = 15,
            MaxDiff = new Rgba32(2, 2, 2, 2),
        };

        public static IReadOnlyDictionary<TexId, TexId> RepresentativeOf
            = Data.File(@"representative.json").LoadJsonFile<Dictionary<TexId, TexId>>();
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
                representative.SaveAsJson(Data.File(@"representative.json", Data.Source.Local));
            };
        }
        public static int CompareIdsByQuality(TexId a, TexId b)
        {
            var q = CompareOnlyQuality(a, b);
            if (q != 0) return q;

            // try not to pick one that isn't used in game
            var u = IsUsedInGame(a).CompareTo(IsUsedInGame(b));
            if (u != 0) return u;

            // if it doesn't matter which one we pick, pick the one with the smaller ID.
            return -a.CompareTo(b);

            static bool IsUsedInGame(TexId id) => DS3.UsedBy.ContainsKey(id);
        }
        internal static int CompareOnlyQuality(TexId a, TexId b)
        {
            if (DS3.OriginalSize.TryGetValue(a, out var aSize) && DS3.OriginalSize.TryGetValue(b, out var bSize))
            {
                var s = aSize.Width.CompareTo(bSize.Width);
                if (s != 0) return s;
            }

            if (DS3.OriginalFormat.TryGetValue(a, out var aFormat) && DS3.OriginalFormat.TryGetValue(b, out var bFormat))
            {
                var af = aFormat.QualityScore;
                var bf = bFormat.QualityScore;
                if (af != null && bf != null && af != bf)
                {
                    return af.Value.CompareTo(bf.Value);
                }
            }

            return 0;
        }

        public static EquivalenceCollection<TexId> AlphaCopiesCertain
            = Data.File(@"alpha-copies-certain.json").LoadJsonFile<EquivalenceCollection<TexId>>();
        internal static UncertainCopies _alphaCopiesConfig = new UncertainCopies()
        {
            CertainFile = @"alpha-copies-certain.json",
            UncertainFile = @"alpha-copies-uncertain.json",
            RejectedFile = @"alpha-copies-rejected.json",
            CopyFilter = id =>
            {
                // ignore all images that are just solid colors
                if (id.IsSolidColor()) return false;
                // ignore all images that aren't transparent
                var t = id.GetTransparency();
                if (t != TransparencyKind.Binary && t != TransparencyKind.Full) return false;

                return true;
            },
            CopyHasherFactory = Hasher.Alpha(),
            CopySpread = image => image.Count <= 64 * 64 ? 16 : 10,
            MaxEqClassSize = 10,
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
            = Data.File(@"alpha-representative.json").LoadJsonFile<Dictionary<TexId, TexId>>();
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
                representative.SaveAsJson(Data.File(@"alpha-representative.json", Data.Source.Local));
            };
        }

        public static EquivalenceCollection<TexId> NormalCopiesCertain
            = Data.File(@"normal-copies-certain.json").LoadJsonFile<EquivalenceCollection<TexId>>();
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
            CopyHasherFactory = Hasher.Normal(),
            CopySpread = image => image.Count <= 64 * 64 ? 9 : image.Count <= 128 * 128 ? 7 : 5,
            MaxDiff = new Rgba32(2, 2, 255, 255),
            ModifyImage = image =>
            {
                image.Multiply(new Rgba32(255, 255, 0, 0));
                image.SetAlpha(255);
            },
        };

        public static IReadOnlyDictionary<TexId, TexId> NormalRepresentativeOf
            = Data.File(@"normal-representative.json").LoadJsonFile<Dictionary<TexId, TexId>>();
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
                representative.SaveAsJson(Data.File(@"normal-representative.json", Data.Source.Local));
            };
        }

        public static EquivalenceCollection<TexId> GlossCopiesCertain
            = Data.File(@"gloss-copies-certain.json").LoadJsonFile<EquivalenceCollection<TexId>>();
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
            CopyHasherFactory = Hasher.BlueChannel(),
            CopySpread = image => image.Count <= 64 * 64 ? 10 : image.Count <= 128 * 128 ? 7 : 5,
            MaxEqClassSize = 15,
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
            = Data.File(@"gloss-representative.json").LoadJsonFile<Dictionary<TexId, TexId>>();
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
                representative.SaveAsJson(Data.File(@"gloss-representative.json", Data.Source.Local));
            };
        }

        public static EquivalenceCollection<TexId> BrightnessSimilarCertain
            = Data.File(@"brightness-similar-certain.json").LoadJsonFile<EquivalenceCollection<TexId>>();
        internal static UncertainCopies _brightnessSimilarConfig = new UncertainCopies()
        {
            CertainFile = @"brightness-similar-certain.json",
            UncertainFile = @"brightness-similar-uncertain.json",
            RejectedFile = @"brightness-similar-rejected.json",
            CopyFilter = id =>
            {
                // ignore all images that are just solid colors
                if (id.IsSolidColor()) return false;
                // only normals
                if (id.GetTexKind() != TexKind.Albedo) return false;
                if (id.GetRepresentative() != id) return false;

                return true;
            },
            CopyHasherFactory = Hasher.NormBrightness(),
            CopySpread = image => image.Count <= 64 * 64 ? 7 : image.Count <= 128 * 128 ? 7 : 7,
            MaxDiff = new Rgba32(2, 2, 2, 255),
            ModifyImage = image =>
            {
                foreach (ref var p in image.Data.AsSpan())
                {
                    p.A = 255;
                }
            }
        };

        public static IReadOnlyDictionary<TexId, TexId> BrightnessSimilarRepresentativeOf
            = Data.File(@"brightness-similar-representative.json").LoadJsonFile<Dictionary<TexId, TexId>>();
        public static IReadOnlyCollection<TexId> BrightnessSimilarRepresentatives = BrightnessSimilarRepresentativeOf.Values.ToHashSet();
        internal static Action<SubProgressToken> CreateBrightnessSimilarRepresentativeIndex()
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
                    if (!BrightnessSimilarCertain.TryGetValue(id, out var othersSet) || othersSet.Count == 0) return;

                    var others = othersSet.ToList();
                    others.Sort(CompareIdsByQuality);

                    var r = others.Last();
                    if (CompareOnlyQuality(r, id) == 0) return;

                    lock (representative)
                    {
                        representative[id] = r;
                    }
                });

                token.SubmitStatus("Saving JSON");
                representative.SaveAsJson(Data.File(@"brightness-similar-representative.json"));
            };
        }

        public static IReadOnlyDictionary<TexId, Dictionary<string, HashSet<string>>> UsedBy
            = Data.File(@"usage.json").LoadJsonFile<Dictionary<TexId, Dictionary<string, HashSet<string>>>>();
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
                usage.SaveAsJson(Data.File(@"usage.json", Data.Source.Local));
            };
        }

        public static IReadOnlyCollection<TexId> Unused
            = Data.File(@"unused.json").LoadJsonFile<HashSet<TexId>>();
        internal static Action<SubProgressToken> CreateUnused()
        {
            return token =>
            {
                token.SubmitStatus($"Searching for files");
                var unused = DS3.OriginalSize.Keys
                    .Where(id => (
                        // FG includes tattoos and other cosmetic textures
                        !id.Value.StartsWith("parts/FG_")
                        // some FXR references the texture
                        && !FXRSFX.Contains(id)
                        // referenced in any flver file
                        && !UsedBy.ContainsKey(id)
                    ))
                    .ToList();
                unused.Sort();

                token.SubmitStatus("Saving copies JSON");
                unused.SaveAsJson(Data.File(@"unused.json", Data.Source.Local));
            };
        }

        /// SFX textures referenced by FXR files.
        /// Data can be found here: https://docs.google.com/spreadsheets/d/1gmUiSpJtxFFl0g04MWMIIs37W13Yjp-WUxtbyv99JIQ/edit#gid=31255113
        public static IReadOnlyCollection<TexId> FXRSFX
            = Data.File(@"fxr-textures.json").LoadJsonFile<HashSet<TexId>>();

        public static IReadOnlyDictionary<TexId, TexId> NormalAlbedo
            = Data.File(@"normal-albedo.json").LoadJsonFile<Dictionary<TexId, TexId>>();
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

                token.SubmitStatus($"Analyzing texture files");
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
                // index.SaveAsJson(Data.File(@"normal-albedo.json"));

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

                        if (NormalAlbedo.TryGetValue(n, out var oldA) && byCount.ContainsKey(oldA))
                        {
                            certain[n] = oldA;
                            continue;
                        }

                        ambiguous[n] = byCount;
                    }
                }

                token.SubmitStatus("Saving JSON");
                certain.SaveAsJson(Data.File(@"normal-albedo-auto.json", Data.Source.Local));
                ambiguous.SaveAsJson(Data.File(@"normal-albedo-manual.json", Data.Source.Local));
            };
        }

        public readonly struct AlbedoNormalReflective : IEquatable<AlbedoNormalReflective>
        {
            public TexId A { get; }
            public TexId N { get; }
            public TexId R { get; }

            public AlbedoNormalReflective(TexId a, TexId n, TexId r) => (A, N, R) = (a, n, r);

            public override bool Equals(object? obj) => obj is AlbedoNormalReflective other ? Equals(other) : false;
            public bool Equals(AlbedoNormalReflective other) => A == other.A && N == other.N && R == other.R;
            public override int GetHashCode() => HashCode.Combine(A, N, R);

            public void Deconstruct(out TexId a, out TexId n, out TexId r) => (a, n, r) = (A, N, R);

            public static implicit operator AlbedoNormalReflective((TexId a, TexId n, TexId r) tuple)
                => new AlbedoNormalReflective(tuple.a, tuple.n, tuple.r);
        }

        public static IReadOnlyDictionary<TexId, AlbedoNormalReflective> ReflectiveANR
            = Data.File(@"reflective-anr.json").LoadJsonFile<Dictionary<TexId, AlbedoNormalReflective>>();
        internal static Action<SubProgressToken> CreateReflectiveANRIndex()
        {
            return token =>
            {
                List<AlbedoNormalReflective> anrIndex = new List<AlbedoNormalReflective>();

                void AddToIndex(
                    (IReadOnlyCollection<TexId> ids, Vector2 scale) albedo,
                    (IReadOnlyCollection<TexId> ids, Vector2 scale) normal,
                    (IReadOnlyCollection<TexId> ids, Vector2 scale) reflective
                )
                {
                    if (albedo.scale != normal.scale || albedo.scale != reflective.scale) return;

                    foreach (var a in albedo.ids)
                    {
                        if (a.IsSolidColor()) continue;
                        foreach (var n in normal.ids)
                        {
                            if (n.IsSolidColor()) continue;
                            foreach (var r in reflective.ids)
                            {
                                if (r.IsSolidColor()) continue;

                                if (!DS3.OriginalSize.TryGetValue(a, out var aSize)) continue;
                                if (!DS3.OriginalSize.TryGetValue(n, out var nSize)) continue;
                                if (!DS3.OriginalSize.TryGetValue(r, out var rSize)) continue;

                                var aRatio = SizeRatio.Of(aSize);
                                var nRatio = SizeRatio.Of(nSize);
                                var rRatio = SizeRatio.Of(rSize);

                                if (aRatio == nRatio && aRatio == rRatio)
                                    anrIndex.Add((a, n, r));
                            }
                        }
                    }
                };

                token.SubmitStatus($"Analyzing flver files");
                token.Reserve(0.5).ForAll(DS3.ReadAllFlverMaterialInfo(), info =>
                {
                    var a = new List<((IReadOnlyCollection<TexId>, Vector2) id, string type)>();
                    var n = new List<((IReadOnlyCollection<TexId>, Vector2) id, string type)>();
                    var r = new List<((IReadOnlyCollection<TexId>, Vector2) id, string type)>();
                    foreach (var mat in info.Materials)
                    {
                        a.Clear();
                        n.Clear();
                        r.Clear();
                        foreach (var tex in mat.Textures)
                        {
                            var i = TexId.FromTexturePath(tex, info.FlverPath);

                            var kind = DS3.TextureTypeToTexKind.GetOrDefault(tex.Type, TexKind.Unknown);
                            if (kind == TexKind.Albedo)
                                a.Add(((i, tex.Scale), tex.Type));
                            else if (kind == TexKind.Normal)
                                n.Add(((i, tex.Scale), tex.Type));
                            else if (kind == TexKind.Reflective)
                                r.Add(((i, tex.Scale), tex.Type));
                        }

                        if (a.Count == 0 || n.Count == 0 || r.Count == 0) continue;

                        // There is a pattern where they types would end with _0, _1, and so on.
                        for (int i = 0; i < 10; i++)
                        {
                            static bool TryGetSuffix(List<((IReadOnlyCollection<TexId>, Vector2) id, string type)> list, string suffix, out int index)
                            {
                                index = -1;
                                for (int i = 0; i < list.Count; i++)
                                {
                                    if (list[i].type.EndsWith(suffix))
                                    {
                                        index = i;
                                        return true;
                                    }
                                }
                                return false;
                            }

                            var suffix = $"_{i}";
                            if (!TryGetSuffix(a, suffix, out var aIndex)) continue;
                            if (!TryGetSuffix(n, suffix, out var nIndex)) continue;
                            if (!TryGetSuffix(r, suffix, out var rIndex)) continue;

                            AddToIndex(a[aIndex].id, n[nIndex].id, r[rIndex].id);
                            a.RemoveAt(aIndex);
                            n.RemoveAt(nIndex);
                            r.RemoveAt(rIndex);
                        }

                        if (a.Count == 0 || n.Count == 0 || r.Count == 0) continue;

                        if (a.Count == 1 && n.Count == 1 && r.Count == 1)
                        {
                            // simple case
                            AddToIndex(a[0].id, n[0].id, r[0].id);
                            continue;
                        }

                        if (a.Count == 2 && n.Count == 2 && r.Count == 2 &&
                            a[0].type.EndsWith("Texture") && n[0].type.EndsWith("Texture") && r[0].type.EndsWith("Texture") &&
                            a[1].type.EndsWith("Texture2") && n[1].type.EndsWith("Texture2") && r[1].type.EndsWith("Texture2"))
                        {
                            AddToIndex(a[0].id, n[0].id, r[0].id);
                            AddToIndex(a[1].id, n[1].id, r[1].id);
                            continue;
                        }

                        if (a.Count == 2 && n.Count == 3 && r.Count == 2 &&
                            mat.MTD.Contains("_Water", StringComparison.OrdinalIgnoreCase) &&
                            a[0].type.EndsWith("Texture") && n[0].type.EndsWith("Texture") && r[0].type.EndsWith("Texture") &&
                            a[1].type.EndsWith("Texture2") && n[1].type.EndsWith("Texture2") && r[1].type.EndsWith("Texture2") &&
                            n[2].type.EndsWith("Texture3"))
                        {
                            AddToIndex(a[0].id, n[0].id, r[0].id);
                            AddToIndex(a[1].id, n[1].id, r[1].id);
                            continue;
                        }
                    }
                });

                token.SubmitStatus($"Categorizing triplets");
                anrIndex = anrIndex.Select(t => new AlbedoNormalReflective(
                    t.A.GetRepresentative(),
                    t.N.GetGlossRepresentative(),
                    t.R.GetRepresentative()
                )).ToHashSet().ToList();

                var known = anrIndex
                    .GroupBy(t => t.R)
                    .SelectMany(g =>
                    {
                        var set = g.ToHashSet();
                        if (set.Count == 1) return set.ToArray();

                        var r = g.Key;
                        if (r.Name.EndsWith("_r"))
                        {
                            var baseName = r.Name.Slice(0, r.Name.Length - 1).ToString();
                            var a = new TexId(r.Category, baseName + "a").GetRepresentative();
                            var n = new TexId(r.Category, baseName + "n").GetGlossRepresentative();
                            AlbedoNormalReflective anr = (a, n, r);
                            if (set.Contains(anr)) return new[] { anr };
                        }

                        return new AlbedoNormalReflective[] { };
                    })
                    .ToHashSet();

                // Some triplets are still left, but this only affects 9 r textures. It really doesn't matter.

                known.ToDictionary(t => t.R).SaveAsJson(Data.File(@"reflective-anr.json", Data.Source.Local));
            };
        }

        public static IEnumerable<FlverMaterialInfo> ReadAllFlverMaterialInfo()
        {
            foreach (var file in Directory.GetFiles(Data.File(@"materials"), "*.json"))
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
                token.ForAllParallel(Directory.GetFiles(Data.File(@"materials"), "*.json"), file =>
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

                    var targetDir = Data.File(@"textures", Data.Source.Local);
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

        public static IEnumerable<(string name, MTD mtd)> ReadAllMTD(Workspace w)
        {
            var mtdBasePath = Path.Join(w.GameDir, "mtd");
            var mtdDir = Path.Join(mtdBasePath, "allmaterialbnd-mtdbnd-dcx");
            if (!Directory.Exists(mtdDir))
            {
                Yabber.Run(Path.Join(mtdBasePath, "allmaterialbnd.mtdbnd.dcx"));
            }

            foreach (var f in Directory.GetFiles(mtdDir, "*.mtd"))
            {
                yield return (Path.GetFileName(f), MTD.Read(f));
            }
        }
        internal static void CreateMTDJson(Workspace w)
        {
            var data = DS3.ReadAllMTD(w).Select(pair =>
            {
                var (name, mtd) = pair;
                return new
                {
                    Name = name,
                    Description = mtd.Description,
                    ShaderPath = mtd.ShaderPath,
                    Textures = mtd.Textures.Select(t =>
                    {
                        return new
                        {
                            Type = t.Type,
                            UVNumber = t.UVNumber,
                            ShaderDataIndex = t.ShaderDataIndex,
                        };
                    }).ToList(),
                    Params = mtd.Params.ToDictionary(
                        p => p.Name,
                        p => p.Value switch
                        {
                            int i => p.Name switch
                            {
                                "g_BlendMode" => ((MTD.BlendMode)i).ToString(),
                                "g_LightingType" => ((MTD.LightingType)i).ToString(),
                                _ => i
                            },
                            float or bool => p.Value,
                            float[] a => string.Join(" ", a.Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                            int[] a => string.Join(" ", a.Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                            _ => p.Value.ToString()
                        }
                    ),
                };
            }).ToList();
            data.SaveAsJson(Data.File("mtd.json", Data.Source.Local));
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

            public string Read => Data.File(Name, Data.Source.Local);
            public string Write => Data.File(Name, Data.Source.Local);

            public DataFile(string name) { Name = name; }

            public static implicit operator DataFile(string value) => new DataFile(value);
        }

        public DataFile CertainFile { get; set; } = "";
        public DataFile UncertainFile { get; set; } = "";
        public DataFile RejectedFile { get; set; } = "";

        public bool SameKind { get; set; } = false;
        public IHasherFactory CopyHasherFactory { get; set; } = Hasher.Rgba();
        public Predicate<TexId> CopyFilter { get; set; } = id => true;
        public Func<ArrayTextureMap<Rgba32>, int> CopySpread { get; set; } = image => 2;
        public Rgba32 MaxDiff { get; set; } = default;
        public Action<ArrayTextureMap<Rgba32>>? ModifyImage { get; set; } = null;

        public int MaxEqClassSize { get; set; } = 10;

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

        internal EquivalenceCollection<TexId> LoadCertain()
        {
            return Load<EquivalenceCollection<TexId>>(CertainFile.Read);
        }
        internal EquivalenceCollection<TexId> LoadUncertain()
        {
            return Load<EquivalenceCollection<TexId>>(UncertainFile.Read);
        }
        private DifferenceCollection<TexId> LoadRejected()
        {
            return Load<DifferenceCollection<TexId>>(RejectedFile.Read);
        }
        private static T Load<T>(string file)
            where T : new()
        {
            if (!File.Exists(file)) return new T();
            return file.LoadJsonFile<T>();
        }

        private static TexId SelectRep(EquivalenceCollection<TexId> eq, TexId id)
        {
            var set = eq.Get(id);
            if (set.Count == 1) return id;
            var list = set.ToList();
            list.Sort();
            return list.MaxBy(id => DS3.OriginalSize[id].Width);
        }

        public Action<SubProgressToken> CreateCopyIndex(Workspace w)
        {
            return token =>
            {
                token.SubmitStatus("Searching for files");
                var files = Directory.GetFiles(w.ExtractDir, "*.dds", SearchOption.AllDirectories)
                    .Where(f => CopyFilter(TexId.FromPath(f)))
                    .ToArray();

                var certain = LoadCertain();
                var final = new EquivalenceCollection<TexId>(certain.Classes);

                const int MaxNumberOfPasses = 4;
                for (int pass = 1; true; pass++)
                {
                    var mixPixelScale = (int)Math.Pow(4, pass - 1);
                    var index = CopyIndex.Create(token.Reserve(0.5), files, r => CopyHasherFactory.Create(r, mixPixelScale));

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

                    var classes = copies.Classes
                        .Select(c =>
                        {
                            var unique = c.Select(id => SelectRep(certain, id)).ToHashSet().Count;
                            return (c, unique);
                        })
                        .ToList();
                    var largeClasses = classes.Where(p => p.unique > MaxEqClassSize).Select(p => p.c).ToList();
                    var smallClasses = classes.Where(p => p.unique <= MaxEqClassSize).Select(p => p.c).ToList();

                    if (pass < MaxNumberOfPasses && largeClasses.Count > 0)
                    {
                        // only add small eq classes and try to further split large classes
                        final.Set(smallClasses);

                        files = largeClasses.SelectMany(c => c).Select(w.GetExtractPath).ToArray();
                    }
                    else
                    {
                        final.Set(copies);
                        break;
                    }
                }

                token.SubmitStatus("Saving JSON");
                final.SaveAsJson(UncertainFile.Write);
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

                token.SubmitStatus("Copying files");
                token.ForAllParallel(classes.Select((x, i) => (x, i)), pair =>
                {
                    var (eqClass, i) = pair;

                    // Remove identical textures
                    eqClass = eqClass.Select(id => SelectRep(certain, id)).ToHashSet().ToList();

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
                            if (width < largest) image = image.UpSample(largest / image.Width, BiCubic.Rgba);
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

                var rejected = DifferenceCollection<TexId>.FromUncertain(LoadRejected(), LoadUncertain(), newCertain);
                rejected.SaveAsJson(RejectedFile.Write);
            };
        }

        public Action<SubProgressToken> UpdateRejected()
        {
            return token =>
            {
                var rejected = DifferenceCollection<TexId>.FromUncertain(LoadRejected(), LoadUncertain(), LoadCertain());
                rejected.SaveAsJson(RejectedFile.Write);
            };
        }

        public Action<SubProgressToken> ManuallyMakeEqual(EquivalenceCollection<TexId> certain)
        {
            return token =>
            {
                certain.Set(LoadCertain());
                certain.SaveAsJson(CertainFile.Write);

                var rejected = DifferenceCollection<TexId>.FromUncertain(LoadRejected(), LoadUncertain(), certain);
                rejected.SaveAsJson(RejectedFile.Write);
            };
        }
    }
}
