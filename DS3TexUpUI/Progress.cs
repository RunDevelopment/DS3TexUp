using System;
using System.Collections.Generic;
using System.Linq;

namespace DS3TexUpUI
{

    [Serializable]
    public class CanceledException : Exception
    {
        public CanceledException() { }
        public CanceledException(string message) : base(message) { }
        public CanceledException(string message, Exception inner) : base(message, inner) { }
        protected CanceledException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }

    public interface IProgressToken
    {
        object Lock { get; }
        bool IsCanceled { get; }
        void CheckCanceled();
        void SubmitStatus(string status);
        void SubmitProgress(double current);
    }

    public class SubProgressToken : IProgressToken
    {
        private readonly IProgressToken _token;
        private double _start;
        private double _size;

        public object Lock => _token.Lock;

        public SubProgressToken(IProgressToken token) : this(token, 0) { }
        public SubProgressToken(IProgressToken token, double start) : this(token, start, 1 - start) { }
        public SubProgressToken(IProgressToken token, double start, double size)
        {
            _token = token;
            _start = start;
            _size = size;
        }

        public bool IsCanceled => _token.IsCanceled;
        public void CheckCanceled() => _token.CheckCanceled();

        public void SubmitProgress(double current)
        {
            _token.SubmitProgress(_start + current * _size);
        }

        public void SubmitStatus(string status)
        {
            _token.SubmitStatus(status);
        }

        public SubProgressToken Reserve(double size)
        {
            size *= _size;
            _start += size;
            _size -= size;
            return new SubProgressToken(_token, _start - size, size);
        }
        public SubProgressToken Slice(double start, double size)
        {
            return new SubProgressToken(_token, _start + start * _size, size * _size);
        }
    }

    public class NoopProgressToken : IProgressToken
    {
        public object Lock => this;
        public bool IsCanceled => false;
        public void CheckCanceled() { }
        public void SubmitProgress(double current) { }
        public void SubmitStatus(string status) { }
    }

    public static class ProgressExtensions
    {
        public static void ForAll<T>(this IProgressToken token, IEnumerable<T> iter, Action<T> action)
        {
            var collection = iter is IReadOnlyCollection<T> coll ? coll : iter.ToList();
            ForAll(token, collection, collection.Count, item => { action(item); return 1; });
        }
        public static void ForAll<T>(this IProgressToken token, IEnumerable<T> iter, int total, Func<T, int> action)
        {
            var done = 0;

            foreach (var item in iter)
            {

                if (token.IsCanceled) return;

                var work = action(item);

                if (token.IsCanceled) return;
                done += work;
                token.SubmitProgress(Math.Clamp(done / (double)total, 0, 1));
            }

            token.SubmitProgress(1);
        }
        public static void ForAll<T>(this IProgressToken token, IEnumerable<T> iter, Action<SubProgressToken, T> action)
        {
            var collection = iter is IReadOnlyCollection<T> coll ? coll : iter.ToList();

            var progress = new SubProgressToken(token);
            var done = 0;
            var factor = 1.0 / collection.Count;
            ForAll(token, collection, item =>
            {
                action(progress.Slice(done * factor, factor), item);
                done++;
            });
        }

        public static void ForAllParallel<T>(this IProgressToken token, IEnumerable<T> iter, Action<T> action)
        {
            var collection = iter is IReadOnlyCollection<T> coll ? coll : iter.ToList();

            ForAllParallel(token, collection, collection.Count, (item) =>
            {
                action(item);
                return 1;
            });
        }
        public static void ForAllParallel<T>(this IProgressToken token, IEnumerable<T> iter, int total, Func<T, int> action)
        {
            token.SubmitProgress(0);


            int done = 0;
            System.Threading.Tasks.Parallel.ForEach(iter, (item, loop) =>
            {
                try
                {
                    lock (token.Lock)
                    {
                        if (token.IsCanceled)
                        {
                            loop.Break();
                            return;
                        }
                    }

                    var work = action(item);

                    lock (token.Lock)
                    {
                        if (token.IsCanceled)
                        {
                            loop.Break();
                            return;
                        }

                        done += work;
                        if (total > 0) token.SubmitProgress(Math.Clamp(done / (double)total, 0, 1));
                    }
                }
                catch (Exception e)
                {
                    if (e is CanceledException)
                    {
                        loop.Break();
                        return;
                    }

                    throw;
                }
            });

            token.CheckCanceled();

            token.SubmitProgress(1);
        }

        public static SubProgressToken[] SplitEqually(this IProgressToken token, int parts)
        {
            var results = new SubProgressToken[parts];

            for (int i = 0; i < parts; i++)
            {
                results[i] = new SubProgressToken(token, i / (double)parts, 1.0 / parts);
            }

            return results;
        }
        public static void SplitEqually(this IProgressToken token, params Action<SubProgressToken>[] consumers)
        {
            var tokens = token.SplitEqually(consumers.Length);

            for (int i = 0; i < consumers.Length; i++)
                consumers[i](tokens[i]);
        }

        public static (SubProgressToken, SubProgressToken) Split(this IProgressToken token, double s1)
        {
            return (
                new SubProgressToken(token, 0, s1),
                new SubProgressToken(token, s1, 1 - s1)
            );
        }
        public static (SubProgressToken, SubProgressToken, SubProgressToken) Split(this IProgressToken token, double s1, double s2)
        {
            return (
                new SubProgressToken(token, 0, s1),
                new SubProgressToken(token, s1, s2),
                new SubProgressToken(token, s1 + s2, 1 - (s1 + s2))
            );
        }
        public static (SubProgressToken, SubProgressToken, SubProgressToken, SubProgressToken) Split(this IProgressToken token, double s1, double s2, double s3)
        {
            return (
                new SubProgressToken(token, 0, s1),
                new SubProgressToken(token, s1, s2),
                new SubProgressToken(token, s1 + s2, s3),
                new SubProgressToken(token, s1 + s2 + s3, 1 - (s1 + s2 + s3))
            );
        }
    }
}
