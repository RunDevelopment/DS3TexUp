using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace DS3TexUpUI
{
    public interface IAverageAccumulator<T>
        where T : struct
    {
        T Result { get; }
        void Add(T value);
    }

    public readonly struct AverageAccumulatorFactory<T, A>
        where T : struct
        where A : IAverageAccumulator<T>, new()
    {
        public A Create() => new A();
    }

    public static class Average
    {
        public static readonly AverageAccumulatorFactory<byte, ByteAverageAccumulator> Byte = default;
        public static readonly AverageAccumulatorFactory<int, IntAverageAccumulator> Int = default;
        public static readonly AverageAccumulatorFactory<float, FloatAverageAccumulator> Float = default;
        public static readonly AverageAccumulatorFactory<Normal, NormalAverageAccumulator> Normal = default;
    }

    public struct ByteAverageAccumulator : IAverageAccumulator<byte>
    {
        private int _total;
        private int _count;

        public byte Result => _count == 0 ? (byte)0 : (byte)(_total / (double)_count);

        public void Add(byte value)
        {
            _total += value;
            _count++;
        }
    }
    public struct IntAverageAccumulator : IAverageAccumulator<int>
    {
        private long _total;
        private int _count;

        public int Result => _count == 0 ? 0 : (int)(_total / (double)_count);

        public void Add(int value)
        {
            _total += value;
            _count++;
        }
    }
    public struct FloatAverageAccumulator : IAverageAccumulator<float>
    {
        private float _total;
        private int _count;

        public float Result => _count == 0 ? 0 : (_total / (float)_count);

        public void Add(float value)
        {
            _total += value;
            _count++;
        }
    }
    public struct NormalAverageAccumulator : IAverageAccumulator<Normal>
    {
        private Vector3 _total;

        public Normal Result => Normal.FromVector(_total);

        public void Add(Normal value)
        {
            _total += value;
        }
    }
    public struct Rgb24AverageAccumulator : IAverageAccumulator<Rgb24>
    {
        private int _totalR;
        private int _totalG;
        private int _totalB;
        private int _count;

        public Rgb24 Result
        {
            get
            {
                if (_count == 0) return new Rgb24(0, 0, 0);

                var factor = (double)_count;
                return new Rgb24(
                    (byte)(_totalR * factor),
                    (byte)(_totalG * factor),
                    (byte)(_totalB * factor)
                );
            }
        }

        public void Add(Rgb24 value)
        {
            _totalR += value.R;
            _totalG += value.G;
            _totalB += value.B;
            _count++;
        }
    }
    public struct Rgba32AverageAccumulator : IAverageAccumulator<Rgba32>
    {
        private int _totalR;
        private int _totalG;
        private int _totalB;
        private int _totalA;
        private int _count;

        public Rgba32 Result
        {
            get
            {
                if (_count == 0) return new Rgba32(0, 0, 0, 0);

                var factor = (double)_count;
                return new Rgba32(
                    (byte)(_totalR * factor),
                    (byte)(_totalG * factor),
                    (byte)(_totalB * factor),
                    (byte)(_totalA * factor)
                );
            }
        }

        public void Add(Rgba32 value)
        {
            _totalR += value.R;
            _totalG += value.G;
            _totalB += value.B;
            _totalA += value.A;
            _count++;
        }
    }
}
