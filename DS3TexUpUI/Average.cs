using System;
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
        public static readonly AverageAccumulatorFactory<Rgb24, RgbAverageAccumulator> Rgb24 = default;
        public static readonly AverageAccumulatorFactory<Rgba32, RgbAverageAccumulator> Rgba32 = default;
        public static readonly AverageAccumulatorFactory<Rgb24, RgbGammaCorrectedPremultipliedAlphaAverageAccumulator> Rgb24GammaAlpha = default;
        public static readonly AverageAccumulatorFactory<Rgba32, RgbGammaCorrectedPremultipliedAlphaAverageAccumulator> Rgba32GammaAlpha = default;
    }

    public struct ByteAverageAccumulator : IAverageAccumulator<byte>
    {
        private uint _total;
        private uint _count;

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
        private uint _count;

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
        private uint _count;

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
    public struct RgbAverageAccumulator : IAverageAccumulator<Rgba32>, IAverageAccumulator<Rgb24>
    {
        private uint _totalR;
        private uint _totalG;
        private uint _totalB;
        private uint _totalA;
        private uint _count;

        public Rgba32 Result
        {
            get
            {
                if (_count == 0) return new Rgba32(0, 0, 0, 0);

                var factor = 1 / (double)_count;
                return new Rgba32(
                    (byte)(_totalR * factor),
                    (byte)(_totalG * factor),
                    (byte)(_totalB * factor),
                    (byte)(_totalA * factor)
                );
            }
        }
        Rgb24 IAverageAccumulator<Rgb24>.Result
        {
            get
            {
                if (_count == 0) return new Rgb24(0, 0, 0);

                var factor = 1 / (double)_count;
                return new Rgb24(
                    (byte)(_totalR * factor),
                    (byte)(_totalG * factor),
                    (byte)(_totalB * factor)
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
        public void Add(Rgb24 value)
        {
            _totalR += value.R;
            _totalG += value.G;
            _totalB += value.B;
            _totalA += 255;
            _count++;
        }
    }
    public struct RgbGammaCorrectedPremultipliedAlphaAverageAccumulator : IAverageAccumulator<Rgba32>, IAverageAccumulator<Rgb24>
    {
        private ulong _totalR;
        private ulong _totalG;
        private ulong _totalB;
        private uint _totalA;
        private uint _count;

        private static byte Sqrt(double v) => (byte)(int)Math.Sqrt(v);
        public Rgba32 Result
        {
            get
            {
                if (_totalA == 0) return default;

                var factor = 1 / (double)_totalA;
                return new Rgba32(
                    (byte)(Sqrt(_totalR * factor)),
                    (byte)(Sqrt(_totalG * factor)),
                    (byte)(Sqrt(_totalB * factor)),
                    (byte)(_totalA / _count)
                );
            }
        }
        Rgb24 IAverageAccumulator<Rgb24>.Result
        {
            get
            {
                if (_totalA == 0) return default;

                var factor = 1 / (double)_totalA;
                return new Rgb24(
                    (byte)(Sqrt(_totalR * factor)),
                    (byte)(Sqrt(_totalG * factor)),
                    (byte)(Sqrt(_totalB * factor))
                );
            }
        }

        public void Add(Rgba32 value)
        {
            _totalR += (uint)value.R * value.R * value.A;
            _totalG += (uint)value.G * value.G * value.A;
            _totalB += (uint)value.B * value.B * value.A;
            _totalA += value.A;
            _count++;
        }
        public void Add(Rgb24 value)
        {
            _totalR += (uint)value.R * value.R * 255;
            _totalG += (uint)value.G * value.G * 255;
            _totalB += (uint)value.B * value.B * 255;
            _totalA += 255;
            _count++;
        }
    }


    public static class MaxAcc
    {
        public static readonly AverageAccumulatorFactory<float, FloatMaxAccumulator> Float = default;
    }

    public struct FloatMaxAccumulator : IAverageAccumulator<float>
    {
        private float _max;
        private bool _init;

        public float Result => _max;

        public void Add(float value)
        {
            if (!_init) _max = float.NegativeInfinity;
            _init = true;
            _max = MathF.Max(_max, value);
        }
    }

}
