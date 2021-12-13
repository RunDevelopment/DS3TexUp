using System;
using System.Collections.Generic;
using System.Text;

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

    public static class ProgressExtensions
    {
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
