using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

#nullable enable

namespace DS3TexUpUI
{
    class SetEquivalence
    {
        private readonly int[] _indexes;

        public readonly int Count;

        public SetEquivalence(int count)
        {
            Count = count;
            _indexes = new int[count];
            for (var i = 0; i < count; i++)
                _indexes[i] = i;
        }

        public void makeEqual(int a, int b)
        {
            // This works using the following idea:
            //  1. If the eq set of a and b is the same, then we can stop.
            //  2. If indexes[a] < indexes[b], then we want to make
            //     indexes[b] := indexes[a]. However, this means that we lose the
            //     information about the indexes[b]! So we will store
            //     oldB := indexes[b], then indexes[b] := indexes[a], and then
            //     make oldB == a.
            //  3. If indexes[a] > indexes[b], similar to 2.

            var aValue = _indexes[a];
            var bValue = _indexes[b];
            while (aValue != bValue)
            {
                if (aValue < bValue)
                {
                    _indexes[b] = aValue;
                    b = bValue;
                    bValue = _indexes[b];
                }
                else
                {
                    _indexes[a] = bValue;
                    a = aValue;
                    aValue = _indexes[a];
                }
            }
        }


        /// This returns:
        ///
        /// 1. `count`: How many different equivalence classes there are.
        /// 2. `indexes`: A map (array) from each element (index) to the index
        ///    of its equivalence class.
        ///
        /// All equivalence class indexes `indexes[i]` are guaranteed to
        /// be <= `count`.
        public (int count, int[] indexes) getEquivalenceSets()
        {
            var counter = 0;
            for (var i = 0; i < Count; i++)
            {
                if (i == _indexes[i])
                    _indexes[i] = counter++;
                else
                    _indexes[i] = _indexes[_indexes[i]];
            }
            return (counter, _indexes);
        }

        public static List<int>[] MergeOverlapping(IEnumerable<IReadOnlyList<int>> sets, int count)
        {
            var eq = new SetEquivalence(count);

            foreach (var set in sets)
            {
                if (set.Count < 2) continue;
                var first = set[0];
                for (int i = 1; i < set.Count; i++)
                    eq.makeEqual(first, set[i]);
            }

            var eqSet = eq.getEquivalenceSets();

            var classes = new List<int>[eqSet.count];
            for (var i = 0; i < classes.Length; i++)
                classes[i] = new List<int>();

            for (int i = 0; i < eqSet.indexes.Length; i++)
                classes[eqSet.indexes[i]].Add(i);

            return classes;
        }
    }

    class SingleElementCollection<T> : IReadOnlyList<T>
    {
        public T Element { get; set; }
        public T this[int index] => index == 0 ? Element : throw new IndexOutOfRangeException();

        public SingleElementCollection(T item)
        {
            Element = item;
        }

        public int Count => 1;

        public IEnumerator<T> GetEnumerator()
        {
            yield return Element;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public static implicit operator SingleElementCollection<T>(T value) => new SingleElementCollection<T>(value);
    }
    class EmptyCollection<T> : IReadOnlyList<T>
    {
        public static readonly EmptyCollection<T> Instance = new EmptyCollection<T>();

        public T this[int index] => throw new IndexOutOfRangeException();

        private EmptyCollection() { }

        public int Count => 0;

        public IEnumerator<T> GetEnumerator() => Enumerable.Empty<T>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class EquivalenceCollection<T>
        where T : notnull
    {
        private readonly Dictionary<T, HashSet<T>> _data = new Dictionary<T, HashSet<T>>();
        private readonly HashSet<HashSet<T>> _classes = new HashSet<HashSet<T>>();

        public IReadOnlyCollection<IReadOnlyCollection<T>> Classes => _classes;

        public EquivalenceCollection() { }
        public EquivalenceCollection(IEnumerable<IEnumerable<T>> collection)
        {
            foreach (var item in collection)
                Add(item);
        }

        public static EquivalenceCollection<T> FromGroups<K>(IEnumerable<T> collection, Func<T, K> keySelector)
        {
            var c = new EquivalenceCollection<T>();
            foreach (var group in collection.GroupBy(keySelector))
                c.Add(group);
            return c;
        }
        public static EquivalenceCollection<T> FromGroups<I, K>(IEnumerable<I> collection, Func<I, K> keySelector, Func<I, T> itemSelector)
        {
            var c = new EquivalenceCollection<T>();
            foreach (var group in collection.GroupBy(keySelector))
                c.Add(group.Select(itemSelector));
            return c;
        }

        public static EquivalenceCollection<T> FromMapping(IEnumerable<T> collection, Func<T, IEnumerable<T>> getEquivalent)
        {
            var c = new EquivalenceCollection<T>();
            var l = new List<T>();
            foreach (var item in collection)
                c.Set(getEquivalent(item).Prepend(item));
            return c;
        }
        public static EquivalenceCollection<T> FromMapping<I>(IEnumerable<I> collection, Func<I, T> getItem, Func<I, IEnumerable<T>> getEquivalent)
        {
            var c = new EquivalenceCollection<T>();
            var l = new List<T>();
            foreach (var item in collection)
                c.Set(getEquivalent(item).Prepend(getItem(item)));
            return c;
        }

        private void MergeInto(HashSet<T> into, HashSet<T> from)
        {
            if (into != from)
            {
                into.UnionWith(from);
                foreach (var i in into)
                    _data[i] = into;
                _classes.Remove(from);
            }
        }

        private HashSet<T> AddImpl(T item)
        {
            var set = new HashSet<T>() { item };
            _data.Add(item, set);
            _classes.Add(set);
            return set;
        }
        public void Add(T a, T b)
        {
            var set = new HashSet<T>() { a, b };
            if (set.Count == 1) return;
            _data.Add(a, set);

            // inserting b is a bit more tricky because we have to remove a if the insertion fails
            try
            {
                _data.Add(b, set);
            }
            catch (System.Exception)
            {
                // remove a
                _data.Remove(a);
                throw;
            }

            _classes.Add(set);
        }
        public void Add(IEnumerable<T> equivlanceClass)
        {
            HashSet<T>? firstSet = null;

            foreach (var item in equivlanceClass)
            {
                if (firstSet == null)
                {
                    firstSet = AddImpl(item);
                }
                else
                {
                    if (_data.TryGetValue(item, out var itemSet))
                    {
                        if (itemSet != firstSet)
                            throw new ArgumentException("One item of the given equivalence class is already in another equivalence class.");
                    }
                    else
                    {
                        firstSet.Add(item);
                        _data.Add(item, firstSet);
                    }
                }
            }

            if (firstSet != null && firstSet.Count == 1)
            {
                _data.Remove(firstSet.Single());
                _classes.Remove(firstSet);
            }
        }
        public void Add(EquivalenceCollection<T> other)
        {
            foreach (var eqClass in other.Classes)
                Add(eqClass);
        }

        private HashSet<T> SetImpl(T item)
        {
            if (!_data.TryGetValue(item, out var set))
            {
                set = new HashSet<T>() { item };
                _data.Add(item, set);
                _classes.Add(set);
            }
            return set;
        }
        public void Set(T a, T b)
        {
            if (a.Equals(b)) return;

            if (_data.TryGetValue(a, out var aSet))
            {
                if (_data.TryGetValue(b, out var bSet))
                {
                    // both are present
                    MergeInto(aSet, bSet);
                }
                else
                {
                    // a is present, b is not
                    aSet.Add(b);
                    _data.Add(b, aSet);
                }
            }
            else
            {
                if (_data.TryGetValue(b, out var bSet))
                {
                    // b is present, a is not
                    bSet.Add(a);
                    _data.Add(a, bSet);
                }
                else
                {
                    // both are not present
                    var set = new HashSet<T>() { a, b };
                    _data.Add(a, set);
                    _data.Add(b, set);
                    _classes.Add(set);
                }
            }
        }
        public void Set(IEnumerable<T> equivlanceClass)
        {
            HashSet<T>? firstSet = null;

            foreach (var item in equivlanceClass)
            {
                if (firstSet == null)
                {
                    firstSet = SetImpl(item);
                }
                else
                {
                    if (_data.TryGetValue(item, out var itemSet))
                    {
                        MergeInto(firstSet, itemSet);
                    }
                    else
                    {
                        firstSet.Add(item);
                        _data[item] = firstSet;
                    }
                }
            }

            if (firstSet != null && firstSet.Count == 1)
            {
                _data.Remove(firstSet.Single());
                _classes.Remove(firstSet);
            }
        }
        public void Set(EquivalenceCollection<T> other)
        {
            foreach (var eqClass in other.Classes)
                Set(eqClass);
        }

        public IReadOnlyCollection<T> Get(T item)
        {
            if (_data.TryGetValue(item, out var eqClass)) return eqClass;
            return new SingleElementCollection<T>(item);
        }

        public bool TryGetValue(T item, [MaybeNullWhen(false)] out IReadOnlyCollection<T> equivalenceClass)
        {
            if (_data.TryGetValue(item, out var set))
            {
                equivalenceClass = set;
                return true;
            }
            equivalenceClass = default;
            return false;
        }

        public bool AreEqual(T a, T b)
        {
            return a.Equals(b) || _data.TryGetValue(a, out var aSet) && _data.TryGetValue(b, out var bSet) && aSet == bSet;
        }
    }

    public class DifferenceCollection<T> : IReadOnlyDictionary<T, IReadOnlyCollection<T>>
        where T : notnull
    {
        private readonly Dictionary<T, HashSet<T>> _data = new Dictionary<T, HashSet<T>>();

        public IReadOnlyCollection<T> this[T key] => _data[key];

        public IEnumerable<T> Keys => _data.Keys;
        public IEnumerable<IReadOnlyCollection<T>> Values => _data.Values;
        public int Count => _data.Count;

        public static DifferenceCollection<T> FromUncertain(EquivalenceCollection<T> uncertain, EquivalenceCollection<T> certain)
        {
            var d = new DifferenceCollection<T>();
            foreach (var eqClass in uncertain.Classes)
            {
                var array = eqClass.ToArray();
                for (int i = 0; i < array.Length; i++)
                {
                    for (int j = 0; j < array.Length; j++)
                    {
                        var a = array[i];
                        var b = array[j];
                        if (!certain.AreEqual(a, b)) d.Set(a, b);
                    }
                }
            }
            return d;
        }

        public void Set(T a, T b)
        {
            if (a.Equals(b)) throw new ArgumentException("Two equal items cannot be different");
            _data.GetOrAdd(a).Add(b);
            _data.GetOrAdd(b).Add(a);
        }

        public bool ContainsKey(T key) => _data.ContainsKey(key);

        public IEnumerator<KeyValuePair<T, IReadOnlyCollection<T>>> GetEnumerator()
        {
            foreach (var (key, value) in _data)
                yield return new KeyValuePair<T, IReadOnlyCollection<T>>(key, value);
        }
        public bool TryGetValue(T key, [MaybeNullWhen(false)] out IReadOnlyCollection<T> value)
        {
            if (_data.TryGetValue(key, out var set))
            {
                value = set;
                return true;
            }
            value = default;
            return false;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
