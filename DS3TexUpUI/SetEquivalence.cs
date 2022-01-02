using System;
using System.Collections.Generic;

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
}
