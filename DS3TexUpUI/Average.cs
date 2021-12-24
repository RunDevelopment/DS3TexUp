using System.Numerics;

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
        public static AverageAccumulatorFactory<byte, ByteAverageAccumulator> Byte() => default;
        public static AverageAccumulatorFactory<int, IntAverageAccumulator> Int() => default;
        public static AverageAccumulatorFactory<float, FloatAverageAccumulator> Float() => default;
        public static AverageAccumulatorFactory<Normal, NormalAverageAccumulator> Normal() => default;
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
}
