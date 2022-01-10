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
            var last = source.Slice(offset, source.Length - offset);
            if (last.Length > 0) yield return last;
        }

        public static T[] ToArray<T>(this Span<T> span, int start) => span.Slice(start).ToArray();
        public static T[] ToArray<T>(this Span<T> span, int start, int step)
        {
            if (step == 0) throw new ArgumentOutOfRangeException(nameof(step));
            if (step == 1) return span.Slice(start).ToArray();

            var array = new T[span.Length / step];

            span = span.Slice(start);
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = span[i * step];
            }

            return array;
        }

        /// <summary>
        /// Duplicates every element in the span count times.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="span"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public static T[] Duplicate<T>(this Span<T> span, int count)
        {
            if (count == 0) return new T[0];
            if (count == 1) return span.ToArray();

            var array = new T[span.Length * count];
            for (int i = 0; i < span.Length; i++)
            {
                var item = span[i];
                for (int j = 0; j < count; j++)
                    array[i * count + j] = item;
            }
            return array;
        }

        public static T[] Map<S, T>(this Span<S> span, Func<S, T> map)
        {
            var array = new T[span.Length];
            for (int i = 0; i < span.Length; i++)
                array[i] = map(span[i]);
            return array;
        }
        public static T[] MapMany<S, T>(this Span<S> span, Func<S, (T, T)> map)
        {
            var array = new T[span.Length * 2];
            for (int i = 0; i < span.Length; i++)
            {
                var (a, b) = map(span[i]);
                array[i * 2 + 0] = a;
                array[i * 2 + 1] = b;
            }
            return array;
        }
        public static T[] MapMany<S, T>(this Span<S> span, Func<S, (T, T, T)> map)
        {
            var array = new T[span.Length * 3];
            for (int i = 0; i < span.Length; i++)
            {
                var (a, b, c) = map(span[i]);
                array[i * 3 + 0] = a;
                array[i * 3 + 1] = b;
                array[i * 3 + 2] = c;
            }
            return array;
        }
        public static T[] MapMany<S, T>(this Span<S> span, Func<S, (T, T, T, T)> map)
        {
            var array = new T[span.Length * 4];
            for (int i = 0; i < span.Length; i++)
            {
                var (a, b, c, d) = map(span[i]);
                array[i * 4 + 0] = a;
                array[i * 4 + 1] = b;
                array[i * 4 + 2] = c;
                array[i * 4 + 3] = d;
            }
            return array;
        }

        public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key)
            where TValue : new()
        {
            if (!dict.TryGetValue(key, out TValue val))
            {
                val = new TValue();
                dict.Add(key, val);
            }

            return val;
        }
        public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, Func<TValue> supplier)
        {
            if (!dict.TryGetValue(key, out TValue val))
            {
                val = supplier();
                dict.Add(key, val);
            }

            return val;
        }
        public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, Func<TKey, TValue> supplier)
        {
            if (!dict.TryGetValue(key, out TValue val))
            {
                val = supplier(key);
                dict.Add(key, val);
            }

            return val;
        }

        public static TValue GetOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dict, TKey key, TValue defaultValue)
        {
            if (dict.TryGetValue(key, out TValue val))
                return val;
            return defaultValue;
        }
        public static TValue GetOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dict, TKey key, Func<TValue> defaultSupplier)
        {
            if (dict.TryGetValue(key, out TValue val))
                return val;
            return defaultSupplier();
        }

        public static bool IsPowerOfTwo(this ulong x)
        {
            return (x != 0) && ((x & (x - 1)) == 0);
        }
        public static bool IsPowerOfTwo(this uint x)
        {
            return (x != 0) && ((x & (x - 1)) == 0);
        }
        public static bool IsPowerOfTwo(this long x)
        {
            return x > 0 && (x != 0) && ((x & (x - 1)) == 0);
        }
        public static bool IsPowerOfTwo(this int x)
        {
            return x > 0 && (x != 0) && ((x & (x - 1)) == 0);
        }

        public static float ExtendOut(this float f, float eMin, float eMax)
        {
            return Math.Clamp((f - eMin) / (eMax - eMin), 0, 1);
        }
        public static double ExtendOut(this double f, double eMin, double eMax)
        {
            return Math.Clamp((f - eMin) / (eMax - eMin), 0, 1);
        }
    }
}
