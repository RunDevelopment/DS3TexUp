using System;
using System.Collections.Generic;

namespace DS3TexUpUI
{
    public static class Extensions
    {
        public static T[] Slice<T>(this T[] source, int index, int length)
        {
            T[] slice = new T[length];
            Array.Copy(source, index, slice, 0, length);
            return slice;
        }
        public static IEnumerable<T[]> Chunks<T>(this T[] source, int size)
        {
            var offset = 0;
            while (source.Length - offset > size)
            {
                yield return source.Slice(offset, size);
                offset += size;
            }
            yield return source.Slice(offset, source.Length - offset);
        }
    }
}
