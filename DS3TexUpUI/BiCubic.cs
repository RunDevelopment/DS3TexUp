using System;
using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace DS3TexUpUI
{
    public static class BiCubic
    {
        private static BiCubicFactory<P, P, S> Create<P, S>()
            where P : struct
            where S : IBiCubicSample<P, P>, new()
        {
            return new BiCubicFactory<P, P, S>(x => x);
        }

        public static readonly BiCubicFactory<byte, byte, ByteSample> Byte = Create<byte, ByteSample>();
        public static readonly BiCubicFactory<float, float, FloatSample> Float = Create<float, FloatSample>();
        public static readonly BiCubicFactory<Vector2, Vector2, Vector2Sample> Vector2 = Create<Vector2, Vector2Sample>();
        public static readonly BiCubicFactory<Vector3, Vector3, Vector3Sample> Vector3 = Create<Vector3, Vector3Sample>();
        public static readonly BiCubicFactory<Vector4, Vector4, Vector4Sample> Vector4 = Create<Vector4, Vector4Sample>();
        public static readonly BiCubicFactory<Rgb24, Rgb24, RgbSample> Rgb = Create<Rgb24, RgbSample>();
        public static readonly BiCubicFactory<Rgba32, Rgba32, RgbaSample> Rgba = Create<Rgba32, RgbaSample>();
        public static readonly BiCubicFactory<NormalAngle, NormalAngle, NormalSample> NormalAngle = Create<NormalAngle, NormalSample>();
        public static readonly BiCubicFactory<Normal, NormalAngle, NormalSample> Normal = new BiCubicFactory<Normal, NormalAngle, NormalSample>(map => map.Convert(DS3TexUpUI.NormalAngle.FromNormal));

        internal static float Cubic(float v0, float v1, float v2, float v3, float blend)
        {
            // Cubic spline interpolation
            var a = (-v0 + 3 * v1 - 3 * v2 + v3) * .5f;
            var b = v0 + (-5 * v1 + 4 * v2 - v3) * .5f;
            var c = (v2 - v0) * .5f;
            var d = v1;

            return d + blend * (c + blend * (b + blend * a));
        }
        internal static Vector2 Cubic(Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3, float blend)
        {
            // Cubic spline interpolation
            var a = (-v0 + 3 * v1 - 3 * v2 + v3) * .5f;
            var b = v0 + (-5 * v1 + 4 * v2 - v3) * .5f;
            var c = (v2 - v0) * .5f;
            var d = v1;

            return d + blend * (c + blend * (b + blend * a));
        }
        internal static Vector3 Cubic(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, float blend)
        {
            // Cubic spline interpolation
            var a = (-v0 + 3 * v1 - 3 * v2 + v3) * .5f;
            var b = v0 + (-5 * v1 + 4 * v2 - v3) * .5f;
            var c = (v2 - v0) * .5f;
            var d = v1;

            return d + blend * (c + blend * (b + blend * a));
        }
        internal static Vector4 Cubic(Vector4 v0, Vector4 v1, Vector4 v2, Vector4 v3, float blend)
        {
            // Cubic spline interpolation
            var a = (-v0 + 3 * v1 - 3 * v2 + v3) * .5f;
            var b = v0 + (-5 * v1 + 4 * v2 - v3) * .5f;
            var c = (v2 - v0) * .5f;
            var d = v1;

            return d + blend * (c + blend * (b + blend * a));
        }
    }

    public interface IBiCubicSample<P, I>
        where P : struct
        where I : struct
    {
        void Assign(
            I a00, I a10, I a20, I a30,
            I a01, I a11, I a21, I a31,
            I a02, I a12, I a22, I a32,
            I a03, I a13, I a23, I a33
        );
        P Interpolate(float xBlend, float yBlend);
    }
    public class BiCubicFactory<P, I, S>
        where P : struct
        where I : struct
        where S : IBiCubicSample<P, I>, new()
    {
        private readonly Func<ArrayTextureMap<P>, ArrayTextureMap<I>> Preprocessor;

        public BiCubicFactory(Func<ArrayTextureMap<P>, ArrayTextureMap<I>> preprocessor)
        {
            Preprocessor = preprocessor;
        }

        public ArrayTextureMap<I> Preprocess(ArrayTextureMap<P> map) => Preprocessor(map);
        public S createSample() => new S();
    }

    public struct FloatSample : IBiCubicSample<float, float>
    {
        private CubicSpline r0;
        private CubicSpline r1;
        private CubicSpline r2;
        private CubicSpline r3;

        public void Assign(
            float a00, float a10, float a20, float a30,
            float a01, float a11, float a21, float a31,
            float a02, float a12, float a22, float a32,
            float a03, float a13, float a23, float a33
        )
        {
            r0 = new CubicSpline(a00, a10, a20, a30);
            r1 = new CubicSpline(a01, a11, a21, a31);
            r2 = new CubicSpline(a02, a12, a22, a32);
            r3 = new CubicSpline(a03, a13, a23, a33);
        }

        public float Interpolate(float xBlend, float yBlend)
        {
            return BiCubic.Cubic(
                r0.At(xBlend),
                r1.At(xBlend),
                r2.At(xBlend),
                r3.At(xBlend),
                yBlend
            );
        }
    }
    public struct Vector2Sample : IBiCubicSample<Vector2, Vector2>
    {
        private CubicSpline2 r0;
        private CubicSpline2 r1;
        private CubicSpline2 r2;
        private CubicSpline2 r3;

        public void Assign(
            Vector2 a00, Vector2 a10, Vector2 a20, Vector2 a30,
            Vector2 a01, Vector2 a11, Vector2 a21, Vector2 a31,
            Vector2 a02, Vector2 a12, Vector2 a22, Vector2 a32,
            Vector2 a03, Vector2 a13, Vector2 a23, Vector2 a33
        )
        {
            r0 = new CubicSpline2(a00, a10, a20, a30);
            r1 = new CubicSpline2(a01, a11, a21, a31);
            r2 = new CubicSpline2(a02, a12, a22, a32);
            r3 = new CubicSpline2(a03, a13, a23, a33);
        }

        public Vector2 Interpolate(float xBlend, float yBlend)
        {
            return BiCubic.Cubic(
                r0.At(xBlend),
                r1.At(xBlend),
                r2.At(xBlend),
                r3.At(xBlend),
                yBlend
            );
        }
    }
    public struct Vector3Sample : IBiCubicSample<Vector3, Vector3>
    {
        private CubicSpline3 r0;
        private CubicSpline3 r1;
        private CubicSpline3 r2;
        private CubicSpline3 r3;

        public void Assign(
            Vector3 a00, Vector3 a10, Vector3 a20, Vector3 a30,
            Vector3 a01, Vector3 a11, Vector3 a21, Vector3 a31,
            Vector3 a02, Vector3 a12, Vector3 a22, Vector3 a32,
            Vector3 a03, Vector3 a13, Vector3 a23, Vector3 a33
        )
        {
            r0 = new CubicSpline3(a00, a10, a20, a30);
            r1 = new CubicSpline3(a01, a11, a21, a31);
            r2 = new CubicSpline3(a02, a12, a22, a32);
            r3 = new CubicSpline3(a03, a13, a23, a33);
        }

        public Vector3 Interpolate(float xBlend, float yBlend)
        {
            return BiCubic.Cubic(
                r0.At(xBlend),
                r1.At(xBlend),
                r2.At(xBlend),
                r3.At(xBlend),
                yBlend
            );
        }
    }
    public struct Vector4Sample : IBiCubicSample<Vector4, Vector4>
    {
        private CubicSpline4 r0;
        private CubicSpline4 r1;
        private CubicSpline4 r2;
        private CubicSpline4 r3;

        public void Assign(
            Vector4 a00, Vector4 a10, Vector4 a20, Vector4 a30,
            Vector4 a01, Vector4 a11, Vector4 a21, Vector4 a31,
            Vector4 a02, Vector4 a12, Vector4 a22, Vector4 a32,
            Vector4 a03, Vector4 a13, Vector4 a23, Vector4 a33
        )
        {
            r0 = new CubicSpline4(a00, a10, a20, a30);
            r1 = new CubicSpline4(a01, a11, a21, a31);
            r2 = new CubicSpline4(a02, a12, a22, a32);
            r3 = new CubicSpline4(a03, a13, a23, a33);
        }

        public Vector4 Interpolate(float xBlend, float yBlend)
        {
            return BiCubic.Cubic(
                r0.At(xBlend),
                r1.At(xBlend),
                r2.At(xBlend),
                r3.At(xBlend),
                yBlend
            );
        }
    }

    public struct ByteSample : IBiCubicSample<byte, byte>
    {
        private FloatSample s;

        public void Assign(
            byte a00, byte a10, byte a20, byte a30,
            byte a01, byte a11, byte a21, byte a31,
            byte a02, byte a12, byte a22, byte a32,
            byte a03, byte a13, byte a23, byte a33
        )
        {
            s.Assign(
                a00, a10, a20, a30,
                a01, a11, a21, a31,
                a02, a12, a22, a32,
                a03, a13, a23, a33
            );
        }

        public byte Interpolate(float xBlend, float yBlend)
        {
            return s.Interpolate(xBlend, yBlend).ToByteClamp();
        }
    }
    public struct NormalSample : IBiCubicSample<NormalAngle, NormalAngle>, IBiCubicSample<Normal, NormalAngle>
    {
        private Vector2Sample s;

        public void Assign(
            NormalAngle a00, NormalAngle a10, NormalAngle a20, NormalAngle a30,
            NormalAngle a01, NormalAngle a11, NormalAngle a21, NormalAngle a31,
            NormalAngle a02, NormalAngle a12, NormalAngle a22, NormalAngle a32,
            NormalAngle a03, NormalAngle a13, NormalAngle a23, NormalAngle a33
        )
        {
            static Vector2 ToVec2(NormalAngle a) => new Vector2(a.X, a.Y);
            s.Assign(
                ToVec2(a00), ToVec2(a10), ToVec2(a20), ToVec2(a30),
                ToVec2(a01), ToVec2(a11), ToVec2(a21), ToVec2(a31),
                ToVec2(a02), ToVec2(a12), ToVec2(a22), ToVec2(a32),
                ToVec2(a03), ToVec2(a13), ToVec2(a23), ToVec2(a33)
            );
        }

        public NormalAngle Interpolate(float xBlend, float yBlend)
        {
            var v = s.Interpolate(xBlend, yBlend);
            return new NormalAngle(v.X, v.Y);
        }
        Normal IBiCubicSample<Normal, NormalAngle>.Interpolate(float xBlend, float yBlend)
        {
            return Interpolate(xBlend, yBlend);
        }
    }

    public struct RgbSample : IBiCubicSample<Rgb24, Rgb24>
    {
        private Vector3Sample s;

        public void Assign(
            Rgb24 a00, Rgb24 a10, Rgb24 a20, Rgb24 a30,
            Rgb24 a01, Rgb24 a11, Rgb24 a21, Rgb24 a31,
            Rgb24 a02, Rgb24 a12, Rgb24 a22, Rgb24 a32,
            Rgb24 a03, Rgb24 a13, Rgb24 a23, Rgb24 a33
        )
        {
            static Vector3 ToVec3(Rgb24 a) => new Vector3(a.R, a.G, a.B);
            s.Assign(
                ToVec3(a00), ToVec3(a10), ToVec3(a20), ToVec3(a30),
                ToVec3(a01), ToVec3(a11), ToVec3(a21), ToVec3(a31),
                ToVec3(a02), ToVec3(a12), ToVec3(a22), ToVec3(a32),
                ToVec3(a03), ToVec3(a13), ToVec3(a23), ToVec3(a33)
            );
        }

        public Rgb24 Interpolate(float xBlend, float yBlend)
        {
            var v = s.Interpolate(xBlend, yBlend);
            return new Rgb24(v.X.ToByteClamp(), v.Y.ToByteClamp(), v.Z.ToByteClamp());
        }
    }
    public struct RgbaSample : IBiCubicSample<Rgba32, Rgba32>
    {
        private Vector4Sample s;

        public void Assign(
            Rgba32 a00, Rgba32 a10, Rgba32 a20, Rgba32 a30,
            Rgba32 a01, Rgba32 a11, Rgba32 a21, Rgba32 a31,
            Rgba32 a02, Rgba32 a12, Rgba32 a22, Rgba32 a32,
            Rgba32 a03, Rgba32 a13, Rgba32 a23, Rgba32 a33
        )
        {
            static Vector4 ToVec4(Rgba32 a) => new Vector4(a.R, a.G, a.B, a.A);
            s.Assign(
                ToVec4(a00), ToVec4(a10), ToVec4(a20), ToVec4(a30),
                ToVec4(a01), ToVec4(a11), ToVec4(a21), ToVec4(a31),
                ToVec4(a02), ToVec4(a12), ToVec4(a22), ToVec4(a32),
                ToVec4(a03), ToVec4(a13), ToVec4(a23), ToVec4(a33)
            );
        }

        public Rgba32 Interpolate(float xBlend, float yBlend)
        {
            var v = s.Interpolate(xBlend, yBlend);
            return new Rgba32(v.X.ToByteClamp(), v.Y.ToByteClamp(), v.Z.ToByteClamp(), v.W.ToByteClamp());
        }
    }

    public readonly struct CubicSpline
    {
        private readonly float a;
        private readonly float b;
        private readonly float c;
        private readonly float d;
        public CubicSpline(float v0, float v1, float v2, float v3)
        {
            // Cubic spline interpolation
            a = (-v0 + 3 * v1 - 3 * v2 + v3) * .5f;
            b = v0 + (-5 * v1 + 4 * v2 - v3) * .5f;
            c = (v2 - v0) * .5f;
            d = v1;
        }
        public float At(float blend) => d + blend * (c + blend * (b + blend * a));
    }
    public readonly struct CubicSpline2
    {
        private readonly Vector2 a;
        private readonly Vector2 b;
        private readonly Vector2 c;
        private readonly Vector2 d;
        public CubicSpline2(Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3)
        {
            // Cubic spline interpolation
            a = (-v0 + 3 * v1 - 3 * v2 + v3) * .5f;
            b = v0 + (-5 * v1 + 4 * v2 - v3) * .5f;
            c = (v2 - v0) * .5f;
            d = v1;
        }
        public Vector2 At(float blend) => d + blend * (c + blend * (b + blend * a));
    }
    public readonly struct CubicSpline3
    {
        private readonly Vector3 a;
        private readonly Vector3 b;
        private readonly Vector3 c;
        private readonly Vector3 d;
        public CubicSpline3(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
        {
            // Cubic spline interpolation
            a = (-v0 + 3 * v1 - 3 * v2 + v3) * .5f;
            b = v0 + (-5 * v1 + 4 * v2 - v3) * .5f;
            c = (v2 - v0) * .5f;
            d = v1;
        }
        public Vector3 At(float blend) => d + blend * (c + blend * (b + blend * a));
    }
    public readonly struct CubicSpline4
    {
        private readonly Vector4 a;
        private readonly Vector4 b;
        private readonly Vector4 c;
        private readonly Vector4 d;
        public CubicSpline4(Vector4 v0, Vector4 v1, Vector4 v2, Vector4 v3)
        {
            // Cubic spline interpolation
            a = (-v0 + 3 * v1 - 3 * v2 + v3) * .5f;
            b = v0 + (-5 * v1 + 4 * v2 - v3) * .5f;
            c = (v2 - v0) * .5f;
            d = v1;
        }
        public Vector4 At(float blend) => d + blend * (c + blend * (b + blend * a));
    }
}
