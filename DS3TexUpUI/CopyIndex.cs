using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using SixLabors.ImageSharp.PixelFormats;

#nullable enable

namespace DS3TexUpUI
{
    public class CopyIndex
    {
        public readonly ConcurrentDictionary<SizeRatio, SameRatioCopyIndex> BySize
            = new ConcurrentDictionary<SizeRatio, SameRatioCopyIndex>();

        public IEnumerable<CopyIndexEntry> Entries => BySize.Values.SelectMany(i => i.Entries);

        public void AddImage(string file) => AddImage(file.LoadTextureMap(), file);
        public void AddImage(ArrayTextureMap<Rgba32> image, string file)
        {
            SameRatioCopyIndex.CheckSize(image);

            var r = new SizeRatio(image.Width, image.Height);
            SameRatioCopyIndex index;
            lock (this)
            {
                index = BySize.GetOrAdd(r, r => new SameRatioCopyIndex(r));
            }
            index.AddImage(image, file);
        }

        public List<CopyIndexEntry>? GetSimilar(string file) => GetSimilar(file.LoadTextureMap());
        public List<CopyIndexEntry>? GetSimilar(ArrayTextureMap<Rgba32> image)
        {
            if (BySize.TryGetValue(new SizeRatio(image.Width, image.Height), out var index))
            {
                return index.GetSimilar(image);
            }
            else
            {
                return new List<CopyIndexEntry>();
            }
        }

        public static CopyIndex Load(string file)
        {
            using var f = File.OpenRead(file);
            return Load(f);
        }
        public static CopyIndex Load(Stream file)
        {
            var index = new CopyIndex();

            var l = file.Length;
            while (file.Position < l)
            {
                var i = SameRatioCopyIndex.Load(file);
                index.BySize[i.Ratio] = i;
            }

            return index;
        }
        public void Save(string file)
        {
            using var f = File.Create(file);
            Save(f);
        }
        public void Save(Stream file)
        {
            foreach (var index in BySize.Values)
            {
                index.Save(file);
            }
        }

        public static CopyIndex Create(SubProgressToken token, IEnumerable<string> files)
            => Create(token, files.ToList());
        public static CopyIndex Create(SubProgressToken token, IReadOnlyCollection<string> files)
        {
            var index = new CopyIndex();

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

    [Serializable]
    public class SameRatioCopyIndex
    {
        public readonly SizeRatio Ratio;
        private readonly int _smallWidth;

        public readonly List<CopyIndexEntry> Entries = new List<CopyIndexEntry>();

        private readonly List<int>[] _grid;
        public readonly List<int> Tiny = new List<int>();


        public SameRatioCopyIndex(SizeRatio ratio)
        {
            Ratio = ratio;
            _smallWidth = GetSmallWidth(ratio);

            var rowCount = _smallWidth * _smallWidth * ratio.H / ratio.W * 4;
            _grid = new List<int>[rowCount * 256];
            for (int i = 0; i < _grid.Length; i++)
                _grid[i] = new List<int>();
        }
        private static int GetSmallWidth(SizeRatio ratio)
        {
            const int MinPixels = 256;

            var s = 1;
            while (ratio.W * ratio.H * s * s < MinPixels)
                s *= 2;
            return s * ratio.W;
        }

        private int AddEntry(ArrayTextureMap<Rgba32> image, string file)
        {
            var id = Entries.Count;
            lock (this)
            {
                Entries.Add(new CopyIndexEntry(file, image.Width, image.Height));
            }
            return id;
        }

        public static bool IsSupportedImage(ArrayTextureMap<Rgba32> image)
        {
            return image.Width.IsPowerOfTwo() && image.Height.IsPowerOfTwo();
        }
        public static void CheckSize(ArrayTextureMap<Rgba32> image)
        {
            if (!IsSupportedImage(image))
                throw new ArgumentException("Both the width and the height have to be powers of 2");
        }

        public void AddImage(string file) => AddImage(file.LoadTextureMap(), file);
        public void AddImage(ArrayTextureMap<Rgba32> image, string file)
        {
            if (!Ratio.Equals(new SizeRatio(image.Width, image.Height)))
                throw new ArgumentException("The ration of the image does not match the ratio of the index");
            CheckSize(image);

            var id = AddEntry(image, file);
            var bytes = GetBytes(image);

            lock (this)
            {
                if (bytes == null)
                {
                    Tiny.Add(id);
                    return;
                }

                for (int i = 0; i < bytes.Length; i++)
                    GetGridCell(i, bytes[i]).Add(id);
            }
        }
        private byte[]? GetBytes(ArrayTextureMap<Rgba32> image)
        {
            if (image.Width < _smallWidth || !IsSupportedImage(image))
                return null;

            var small = image.DownSample(Average.Rgba32, image.Width / _smallWidth);
            var bytes = new byte[small.Data.Length * 4];

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

            return bytes;
        }
        private bool TryDownSample(ArrayTextureMap<Rgba32> image, out ArrayTextureMap<Rgba32> small)
        {
            if (image.Width < _smallWidth)
            {
                small = default;
                return false;
            }
            else
            {
                small = image.DownSample(Average.Rgba32, image.Width / _smallWidth);
                return true;
            }
        }

        private List<int> GetGridCell(int row, byte column)
        {
            return _grid[row * 256 + column];
        }

        public List<CopyIndexEntry>? GetSimilar(ArrayTextureMap<Rgba32> image)
        {
            var bytes = GetBytes(image);
            if (bytes == null) return null;

            var acc = GetSimilar(0, bytes[0]);
            for (int i = 1; i < bytes.Length; i++)
                acc.And(GetSimilar(i, bytes[i]));

            var l = new List<CopyIndexEntry>();
            foreach (var id in acc)
                l.Add(Entries[id]);

            return l;
        }
        private SimpleBitSet GetSimilar(int row, byte column)
        {
            var set = new SimpleBitSet(Entries.Count);

            foreach (var id in GetGridCell(row, column))
                set.SetTrue(id);

            if (column > 0)
                foreach (var id in GetGridCell(row, (byte)(column - 1)))
                    set.SetTrue(id);
            if (column < 255)
                foreach (var id in GetGridCell(row, (byte)(column + 1)))
                    set.SetTrue(id);

            return set;
        }

        public static SameRatioCopyIndex Load(Stream stream)
        {
            BinaryFormatter b = new BinaryFormatter();
            return (SameRatioCopyIndex)b.Deserialize(stream);
        }
        public void Save(Stream stream)
        {
            lock (this)
            {
                BinaryFormatter b = new BinaryFormatter();
                b.Serialize(stream, this);
            }
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

    [Serializable]
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

    [Serializable]
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
    }
}
