using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.PixelFormats;

#nullable enable

namespace DS3TexUpUI
{
    public class CopyIndex
    {
        public readonly ConcurrentDictionary<SizeRatio, SameRatioCopyIndex> BySize
            = new ConcurrentDictionary<SizeRatio, SameRatioCopyIndex>();
        public readonly Func<SizeRatio, IImageHasher> HasherFactory;

        public IEnumerable<CopyIndexEntry> Entries => BySize.Values.SelectMany(i => i.Entries);

        public CopyIndex(Func<SizeRatio, IImageHasher> hasherFactory)
        {
            HasherFactory = hasherFactory;
        }

        public bool AddImage(string file) => AddImage(file.LoadTextureMap(), file);
        public bool AddImage(ArrayTextureMap<Rgba32> image, string file)
        {
            if (!image.Width.IsPowerOfTwo() || !image.Height.IsPowerOfTwo())
                return false;

            var r = SizeRatio.Of(image);
            SameRatioCopyIndex index;
            lock (BySize)
            {
                index = BySize.GetOrAdd(r, r => new SameRatioCopyIndex(HasherFactory(r)));
            }
            return index.AddImage(image, file);
        }

        public List<CopyIndexEntry>? GetSimilar(string file) => GetSimilar(file.LoadTextureMap());
        public List<CopyIndexEntry>? GetSimilar(ArrayTextureMap<Rgba32> image, byte spread = 1)
        {
            if (BySize.TryGetValue(SizeRatio.Of(image), out var index))
            {
                return index.GetSimilar(image, spread);
            }
            else
            {
                return new List<CopyIndexEntry>();
            }
        }

        public static CopyIndex Create(SubProgressToken token, IReadOnlyCollection<string> files, Func<SizeRatio, IImageHasher> hasherFactory)
        {
            var index = new CopyIndex(hasherFactory);

            token.SubmitStatus($"Indexing {files.Count} files");
            token.ForAllParallel(files, f =>
            {
                try
                {
                    var image = f.LoadTextureMap();
                    index.AddImage(image, f);
                }
                catch (System.Exception)
                {
                    // ignore
                }
            });

            return index;
        }

        public List<HashSet<string>> GetEquivalenceClasses(SubProgressToken token, IEnumerable<string> files)
            => GetEquivalenceClasses(token, files.ToList());
        public List<HashSet<string>> GetEquivalenceClasses(SubProgressToken token, IReadOnlyList<string> files)
        {
            var fileIds = files.Select((f, i) => (f, i)).ToDictionary(p => p.f, p => p.i);
            var fileSizes = new (int, int)[files.Count];
            foreach (var e in Entries)
            {
                if (fileIds.TryGetValue(e.File, out var id))
                    fileSizes[id] = (e.Width, e.Height);
            }

            var eqRelations = new List<int[]>();

            token.SubmitStatus($"Looking up {files.Count} files");
            token.ForAllParallel(files, f =>
            {
                try
                {
                    var similar = GetSimilar(f);
                    if (similar == null || similar.Count < 2)
                        return;
                    var array = similar.Select(e => fileIds[e.File]).ToArray();

                    lock (eqRelations)
                    {
                        eqRelations.Add(array);
                    }
                }
                catch (System.Exception)
                {
                    // ignore
                }
            });

            token.SubmitStatus("Finding equivalence classes");

            var eqClasses = SetEquivalence.MergeOverlapping(eqRelations, files.Count);

            var lines = eqClasses
                 .Where(e => e.Count >= 2)
                 .Select(e =>
                 {
                     e.Sort((a, b) => fileSizes[a].Item1.CompareTo(fileSizes[b].Item1));
                     return string.Join(";", e.Select(i => files[i]));
                 })
                 .ToList();

            return eqClasses.Select(eq => eq.Select(i => files[i]).ToHashSet()).ToList();
        }
    }

    public class SameRatioCopyIndex
    {
        public readonly IImageHasher Hasher;

        public readonly List<CopyIndexEntry> Entries = new List<CopyIndexEntry>();

        private readonly List<int>[] _grid;

        private readonly (Object obj, int start, int end)[] _gridLocks;

        public SameRatioCopyIndex(IImageHasher hasher)
        {
            Hasher = hasher;
            _grid = new List<int>[hasher.ByteCount * 256];
            for (int i = 0; i < _grid.Length; i++)
                _grid[i] = new List<int>();

            const int Locks = 4;
            _gridLocks = Enumerable.Range(0, Locks)
                .Select(i =>
                {
                    var len = hasher.ByteCount / Locks;
                    var start = i * len;
                    var end = i == Locks - 1 ? hasher.ByteCount : (i + 1) * len;
                    return (new object(), start, end);
                })
                .ToArray();
        }

        private int AddEntry(ArrayTextureMap<Rgba32> image, string file)
        {
            lock (this)
            {
                var id = Entries.Count;
                Entries.Add(new CopyIndexEntry(file, image.Width, image.Height));
                return id;
            }
        }

        public bool AddImage(string file) => AddImage(file.LoadTextureMap(), file);
        public bool AddImage(ArrayTextureMap<Rgba32> image, string file)
        {
            if (Hasher.TryGetBytes(image, out var bytes))
            {
                if (bytes.Length != Hasher.ByteCount)
                    throw new Exception("Invalid number of bytes");

                var id = AddEntry(image, file);
                foreach (var (lockObj, start, end) in _gridLocks)
                {
                    lock (lockObj)
                    {
                        for (int i = start; i < end; i++)
                            GetGridCell(i, bytes[i]).Add(id);
                    }
                }
                return true;
            }
            return false;
        }

        private List<int> GetGridCell(int row, byte column)
        {
            return _grid[row * 256 + column];
        }

        public List<CopyIndexEntry>? GetSimilar(ArrayTextureMap<Rgba32> image, byte spread = 1)
        {
            if (!Hasher.TryGetBytes(image, out var bytes)) return null;

            var acc = GetSimilar(0, bytes[0], spread);
            for (int i = 1; i < bytes.Length; i++)
                acc.And(GetSimilar(i, bytes[i], spread));

            var l = new List<CopyIndexEntry>();
            foreach (var id in acc)
                l.Add(Entries[id]);

            return l;
        }
        private SimpleBitSet GetSimilar(int row, byte column, byte spread)
        {
            var set = new SimpleBitSet(Entries.Count);

            var min = Math.Max(0, column - spread);
            var max = Math.Min(255, column + spread);
            for (int c = min; c <= max; c++)
                foreach (var id in GetGridCell(row, (byte)c))
                    set.SetTrue(id);

            return set;
        }

        private struct SimpleBitSet : IEnumerable<int>
        {
            public int[] Data;

            public bool this[int index]
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    int mask = 1 << (index & 0b11111);
                    return (Data[index >> 5] & mask) != 0;
                }
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                set
                {
                    int mask = 1 << (index & 0b11111);
                    ref var foo = ref Data[index >> 5];
                    foo = value ? foo | mask : foo & ~mask;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public SimpleBitSet(int bitLength)
            {
                Data = new int[bitLength / 32 + 1];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetTrue(int index)
            {
                int mask = 1 << (index & 0b11111);
                Data[index >> 5] |= mask;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetFalse(int index)
            {
                int mask = 1 << (index & 0b11111);
                Data[index >> 5] &= ~mask;
            }

            public void And(SimpleBitSet other)
            {
                var min = Math.Min(Data.Length, other.Data.Length);
                for (int i = 0; i < min; i++)
                    Data[i] &= other.Data[i];
            }

            public IEnumerator<int> GetEnumerator()
            {
                for (int i = 0; i < Data.Length; i++)
                {
                    uint value = unchecked((uint)Data[i]);

                    var offset = i * 32;
                    while (value != 0)
                    {
                        var t = value & unchecked((uint)-value);

                        yield return offset + BitOperations.TrailingZeroCount(value);
                        value ^= t;
                    }
                }
            }
            IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
        }
    }

    public readonly struct SizeInfo
    {
        public int MinPixels { get; }

        public SizeRatio Ratio { get; }
        public int ScaleFactor { get; }

        public int MinWidth => ScaleFactor * Ratio.W;
        public int MinHeight => ScaleFactor * Ratio.H;

        public SizeInfo(SizeRatio ratio, int minPixels)
        {
            MinPixels = minPixels;
            Ratio = ratio;
            ScaleFactor = ratio.GetUpscaleFactor(minPixels);
        }

        public bool Supports(ArrayTextureMap<Rgba32> image)
        {
            return image.Width >= MinWidth
                && Ratio == SizeRatio.Of(image)
                && image.Width.IsPowerOfTwo() && image.Height.IsPowerOfTwo();
        }
    }
    public interface IImageHasher
    {
        SizeInfo Size { get; }
        int ByteCount { get; }
        bool TryGetBytes(ArrayTextureMap<Rgba32> image, out byte[] bytes);
    }
    public interface IHasherFactory
    {
        IImageHasher Create(SizeRatio ratio, int mixPixelScale);
    }
    public static class Hasher
    {
        private class Factory : IHasherFactory
        {
            private int MinPixels;
            private int Channels;
            private Action<ArrayTextureMap<Rgba32>, int, byte[]> Writer;

            public Factory(int minPixels, int channels, Action<ArrayTextureMap<Rgba32>, int, byte[]> writer)
            {
                MinPixels = minPixels;
                Channels = channels;
                Writer = writer;
            }

            public IImageHasher Create(SizeRatio ratio, int mixPixelScale)
            {
                return new Hasher(new SizeInfo(ratio, MinPixels * mixPixelScale), Channels, Writer);
            }

            private class Hasher : IImageHasher
            {
                public SizeInfo Size { get; }
                private int Channels;
                private Action<ArrayTextureMap<Rgba32>, int, byte[]> Writer;

                public int ByteCount => Size.MinWidth * Size.MinHeight * Channels;

                public Hasher(SizeInfo size, int channels, Action<ArrayTextureMap<Rgba32>, int, byte[]> writer)
                {
                    Size = size;
                    Channels = channels;
                    Writer = writer;
                }

                public bool TryGetBytes(ArrayTextureMap<Rgba32> image, out byte[] bytes)
                {
                    if (!Size.Supports(image))
                    {
                        bytes = new byte[0];
                        return false;
                    }

                    bytes = new byte[ByteCount];
                    Writer(image, image.Width / Size.MinWidth, bytes);
                    return true;
                }
            }
        }

        public static IHasherFactory Rgba(int minPixels = 256)
        {
            return new Factory(minPixels, channels: 4, writer: (image, downScale, bytes) =>
            {
                var small = image.DownSample(Average.Rgba32, downScale);

                const float WeightR = 0.25f;
                const float WeightG = 0.5f;
                const float WeightB = 0.25f;
                const float WeightA = 0.25f;

                for (int i = 0; i < small.Data.Length; i++)
                {
                    var p = small.Data[i];

                    bytes[i * 4 + 0] = (byte)(p.R * WeightR);
                    bytes[i * 4 + 1] = (byte)(p.G * WeightG);
                    bytes[i * 4 + 2] = (byte)(p.B * WeightB);
                    bytes[i * 4 + 3] = (byte)(p.A * WeightA);
                }
            });
        }
        public static IHasherFactory Alpha(int minPixels = 256)
        {
            return new Factory(minPixels, channels: 1, writer: (image, downScale, bytes) =>
            {
                var small = image.DownSample(Average.Rgba32, downScale);

                for (int i = 0; i < small.Data.Length; i++)
                    bytes[i] = (byte)(small.Data[i].A >> 2); // divide by 4 to get rid of noise
            });
        }
        public static IHasherFactory Normal(int minPixels = 512)
        {
            return new Factory(minPixels, channels: 2, writer: (image, downScale, bytes) =>
            {
                var small = image.DownSample(Average.Rgba32, downScale);

                for (int i = 0; i < small.Data.Length; i++)
                {
                    bytes[i * 2 + 0] = (byte)(small.Data[i].R >> 1); // divide by 2 to get rid of noise
                    bytes[i * 2 + 1] = (byte)(small.Data[i].G >> 1); // divide by 2 to get rid of noise
                }
            });
        }
        public static IHasherFactory BlueChannel(int minPixels = 512)
        {
            return new Factory(minPixels, channels: 1, writer: (image, downScale, bytes) =>
            {
                var small = image.DownSample(Average.Rgba32, downScale);

                for (int i = 0; i < small.Data.Length; i++)
                    bytes[i] = (byte)(small.Data[i].B >> 1); // divide by 2 to get rid of noise
            });
        }
        public static IHasherFactory NormBrightness(int minPixels = 256)
        {
            return new Factory(minPixels, channels: 1, writer: (image, downScale, bytes) =>
            {
                var small = image.DownSample(Average.Rgba32, downScale);

                for (int i = 0; i < small.Data.Length; i++)
                {
                    bytes[i] = small[i].GetGreyBrightness();
                }

                int avg = 0;
                foreach (var b in bytes) avg += b;
                avg /= bytes.Length;

                var variance = 0;
                foreach (var b in bytes) variance += (avg - b) * (avg - b);

                var sigma = MathF.Sqrt(variance);

                foreach (ref var b in bytes.AsSpan())
                    b = ((b - avg) / (sigma * 2) * 127.5f + 127.5f).ToByteClamp();
            });
        }
    }

    public struct CopyIndexEntry
    {
        public string File;
        public int Width;
        public int Height;

        public CopyIndexEntry(string file, int width, int height)
        {
            File = file;
            Width = width;
            Height = height;
        }
    }

    public readonly struct SizeRatio : IEquatable<SizeRatio>, IComparable<SizeRatio>
    {
        public readonly int W;
        public readonly int H;

        public SizeRatio(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException();

            var gcd = GCD(width, height);
            W = width / gcd;
            H = height / gcd;
        }
        private static int GCD(int a, int b)
        {
            while (a != 0 && b != 0)
            {
                if (a > b)
                    a %= b;
                else
                    b %= a;
            }

            return a | b;
        }

        public static SizeRatio Of<T>(ITextureMap<T> image) where T : struct
            => new SizeRatio(image.Width, image.Height);
        public static SizeRatio Of(SixLabors.ImageSharp.Size size)
            => new SizeRatio(size.Width, size.Height);
        public static SizeRatio Of(System.Drawing.Size size)
            => new SizeRatio(size.Width, size.Height);

        public int CompareTo(SizeRatio other)
        {
            if (W == other.W) return H.CompareTo(other.H);
            return W.CompareTo(other.W);
        }
        public override bool Equals(object? obj)
        {
            if (obj is SizeRatio other) return Equals(other);
            return false;
        }
        public bool Equals(SizeRatio other) => W == other.W && H == other.H;
        public override int GetHashCode() => W << 3 | H;
        public override string ToString() => $"SizeRation {W}:{H}";

        /// <summary> Returns the smallest power of two factor such that W*s*H*s >= minPixels. </summary>
        public int GetUpscaleFactor(int minPixels)
        {
            var s = 1;
            while (W * H * s * s < minPixels)
                s *= 2;
            return s;
        }

        public static bool operator ==(SizeRatio left, SizeRatio right) => left.Equals(right);
        public static bool operator !=(SizeRatio left, SizeRatio right) => !(left == right);
    }
}
