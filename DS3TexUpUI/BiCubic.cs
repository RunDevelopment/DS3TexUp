using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace DS3TexUpUI
{
    public static class BiCubic
    {
        public static readonly ByteInterpolator Byte = new ByteInterpolator();
        public static readonly FloatInterpolator Float = new FloatInterpolator();
        public static readonly Vector2Interpolator Vector2 = new Vector2Interpolator();
        public static readonly Vector3Interpolator Vector3 = new Vector3Interpolator();
        public static readonly Vector4Interpolator Vector4 = new Vector4Interpolator();
        public static readonly RgbInterpolator Rgb = new RgbInterpolator();
        public static readonly RgbaInterpolator Rgba = new RgbaInterpolator();
        public static readonly NormalInterpolator Normal = new NormalInterpolator();
        public static readonly NormalAngleInterpolator NormalAngle = new NormalAngleInterpolator();

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


    public readonly struct NormalInterpolator : IBiCubicInterpolator<Normal, NormalAngle>
    {
        public ArrayTextureMap<NormalAngle> Preprocess(ArrayTextureMap<Normal> map)
            => map.Convert(NormalAngle.FromNormal);
        public Normal BiInterpolate(
            float xBlend, float yBlend,
            NormalAngle a00, NormalAngle a10, NormalAngle a20, NormalAngle a30,
            NormalAngle a01, NormalAngle a11, NormalAngle a21, NormalAngle a31,
            NormalAngle a02, NormalAngle a12, NormalAngle a22, NormalAngle a32,
            NormalAngle a03, NormalAngle a13, NormalAngle a23, NormalAngle a33
        )
        {
            var aX = BiCubic.Cubic(
                BiCubic.Cubic(a00.X, a10.X, a20.X, a30.X, xBlend),
                BiCubic.Cubic(a01.X, a11.X, a21.X, a31.X, xBlend),
                BiCubic.Cubic(a02.X, a12.X, a22.X, a32.X, xBlend),
                BiCubic.Cubic(a03.X, a13.X, a23.X, a33.X, xBlend),
                yBlend
            );
            var aY = BiCubic.Cubic(
                BiCubic.Cubic(a00.Y, a10.Y, a20.Y, a30.Y, xBlend),
                BiCubic.Cubic(a01.Y, a11.Y, a21.Y, a31.Y, xBlend),
                BiCubic.Cubic(a02.Y, a12.Y, a22.Y, a32.Y, xBlend),
                BiCubic.Cubic(a03.Y, a13.Y, a23.Y, a33.Y, xBlend),
                yBlend
            );
            return new NormalAngle(aX, aY);
        }
    }
    public readonly struct NormalAngleInterpolator : IBiCubicInterpolator<NormalAngle>
    {
        public NormalAngle BiInterpolate(
            float xBlend, float yBlend,
            NormalAngle a00, NormalAngle a10, NormalAngle a20, NormalAngle a30,
            NormalAngle a01, NormalAngle a11, NormalAngle a21, NormalAngle a31,
            NormalAngle a02, NormalAngle a12, NormalAngle a22, NormalAngle a32,
            NormalAngle a03, NormalAngle a13, NormalAngle a23, NormalAngle a33
        )
        {
            var aX = BiCubic.Cubic(
                BiCubic.Cubic(a00.X, a10.X, a20.X, a30.X, xBlend),
                BiCubic.Cubic(a01.X, a11.X, a21.X, a31.X, xBlend),
                BiCubic.Cubic(a02.X, a12.X, a22.X, a32.X, xBlend),
                BiCubic.Cubic(a03.X, a13.X, a23.X, a33.X, xBlend),
                yBlend
            );
            var aY = BiCubic.Cubic(
                BiCubic.Cubic(a00.Y, a10.Y, a20.Y, a30.Y, xBlend),
                BiCubic.Cubic(a01.Y, a11.Y, a21.Y, a31.Y, xBlend),
                BiCubic.Cubic(a02.Y, a12.Y, a22.Y, a32.Y, xBlend),
                BiCubic.Cubic(a03.Y, a13.Y, a23.Y, a33.Y, xBlend),
                yBlend
            );
            return new NormalAngle(aX, aY);
        }
    }
    public readonly struct ByteInterpolator : IBiCubicInterpolator<byte>
    {
        public byte BiInterpolate(
            float xBlend, float yBlend,
            byte a00, byte a10, byte a20, byte a30,
            byte a01, byte a11, byte a21, byte a31,
            byte a02, byte a12, byte a22, byte a32,
            byte a03, byte a13, byte a23, byte a33
        )
        {
            return BiCubic.Cubic(
                BiCubic.Cubic(a00, a10, a20, a30, xBlend),
                BiCubic.Cubic(a01, a11, a21, a31, xBlend),
                BiCubic.Cubic(a02, a12, a22, a32, xBlend),
                BiCubic.Cubic(a03, a13, a23, a33, xBlend),
                yBlend
            ).ToByteClamp();
        }
    }
    public readonly struct RgbInterpolator : IBiCubicInterpolator<Rgb24>
    {
        public Rgb24 BiInterpolate(
            float xBlend, float yBlend,
            Rgb24 a00, Rgb24 a10, Rgb24 a20, Rgb24 a30,
            Rgb24 a01, Rgb24 a11, Rgb24 a21, Rgb24 a31,
            Rgb24 a02, Rgb24 a12, Rgb24 a22, Rgb24 a32,
            Rgb24 a03, Rgb24 a13, Rgb24 a23, Rgb24 a33
        )
        {
            var r = BiCubic.Cubic(
                BiCubic.Cubic(a00.R, a10.R, a20.R, a30.R, xBlend),
                BiCubic.Cubic(a01.R, a11.R, a21.R, a31.R, xBlend),
                BiCubic.Cubic(a02.R, a12.R, a22.R, a32.R, xBlend),
                BiCubic.Cubic(a03.R, a13.R, a23.R, a33.R, xBlend),
                yBlend
            );
            var g = BiCubic.Cubic(
                BiCubic.Cubic(a00.G, a10.G, a20.G, a30.G, xBlend),
                BiCubic.Cubic(a01.G, a11.G, a21.G, a31.G, xBlend),
                BiCubic.Cubic(a02.G, a12.G, a22.G, a32.G, xBlend),
                BiCubic.Cubic(a03.G, a13.G, a23.G, a33.G, xBlend),
                yBlend
            );
            var b = BiCubic.Cubic(
                BiCubic.Cubic(a00.B, a10.B, a20.B, a30.B, xBlend),
                BiCubic.Cubic(a01.B, a11.B, a21.B, a31.B, xBlend),
                BiCubic.Cubic(a02.B, a12.B, a22.B, a32.B, xBlend),
                BiCubic.Cubic(a03.B, a13.B, a23.B, a33.B, xBlend),
                yBlend
            );
            return new Rgb24(r.ToByteClamp(), g.ToByteClamp(), b.ToByteClamp());
        }
    }
    public readonly struct RgbaInterpolator : IBiCubicInterpolator<Rgba32>
    {
        public Rgba32 BiInterpolate(
            float xBlend, float yBlend,
            Rgba32 a00, Rgba32 a10, Rgba32 a20, Rgba32 a30,
            Rgba32 a01, Rgba32 a11, Rgba32 a21, Rgba32 a31,
            Rgba32 a02, Rgba32 a12, Rgba32 a22, Rgba32 a32,
            Rgba32 a03, Rgba32 a13, Rgba32 a23, Rgba32 a33
        )
        {
            var r = BiCubic.Cubic(
                BiCubic.Cubic(a00.R, a10.R, a20.R, a30.R, xBlend),
                BiCubic.Cubic(a01.R, a11.R, a21.R, a31.R, xBlend),
                BiCubic.Cubic(a02.R, a12.R, a22.R, a32.R, xBlend),
                BiCubic.Cubic(a03.R, a13.R, a23.R, a33.R, xBlend),
                yBlend
            );
            var g = BiCubic.Cubic(
                BiCubic.Cubic(a00.G, a10.G, a20.G, a30.G, xBlend),
                BiCubic.Cubic(a01.G, a11.G, a21.G, a31.G, xBlend),
                BiCubic.Cubic(a02.G, a12.G, a22.G, a32.G, xBlend),
                BiCubic.Cubic(a03.G, a13.G, a23.G, a33.G, xBlend),
                yBlend
            );
            var b = BiCubic.Cubic(
                BiCubic.Cubic(a00.B, a10.B, a20.B, a30.B, xBlend),
                BiCubic.Cubic(a01.B, a11.B, a21.B, a31.B, xBlend),
                BiCubic.Cubic(a02.B, a12.B, a22.B, a32.B, xBlend),
                BiCubic.Cubic(a03.B, a13.B, a23.B, a33.B, xBlend),
                yBlend
            );
            var a = BiCubic.Cubic(
                BiCubic.Cubic(a00.A, a10.A, a20.A, a30.A, xBlend),
                BiCubic.Cubic(a01.A, a11.A, a21.A, a31.A, xBlend),
                BiCubic.Cubic(a02.A, a12.A, a22.A, a32.A, xBlend),
                BiCubic.Cubic(a03.A, a13.A, a23.A, a33.A, xBlend),
                yBlend
            );
            return new Rgba32(r.ToByteClamp(), g.ToByteClamp(), b.ToByteClamp(), a.ToByteClamp());
        }
    }
    public readonly struct FloatInterpolator : IBiCubicInterpolator<float>
    {
        public float BiInterpolate(
            float xBlend, float yBlend,
            float a00, float a10, float a20, float a30,
            float a01, float a11, float a21, float a31,
            float a02, float a12, float a22, float a32,
            float a03, float a13, float a23, float a33
        )
        {
            return BiCubic.Cubic(
                BiCubic.Cubic(a00, a10, a20, a30, xBlend),
                BiCubic.Cubic(a01, a11, a21, a31, xBlend),
                BiCubic.Cubic(a02, a12, a22, a32, xBlend),
                BiCubic.Cubic(a03, a13, a23, a33, xBlend),
                yBlend
            );
        }
    }
    public readonly struct Vector2Interpolator : IBiCubicInterpolator<Vector2>
    {
        public Vector2 BiInterpolate(
            float xBlend, float yBlend,
            Vector2 a00, Vector2 a10, Vector2 a20, Vector2 a30,
            Vector2 a01, Vector2 a11, Vector2 a21, Vector2 a31,
            Vector2 a02, Vector2 a12, Vector2 a22, Vector2 a32,
            Vector2 a03, Vector2 a13, Vector2 a23, Vector2 a33
        )
        {
            return BiCubic.Cubic(
                BiCubic.Cubic(a00, a10, a20, a30, xBlend),
                BiCubic.Cubic(a01, a11, a21, a31, xBlend),
                BiCubic.Cubic(a02, a12, a22, a32, xBlend),
                BiCubic.Cubic(a03, a13, a23, a33, xBlend),
                yBlend
            );
        }
    }
    public readonly struct Vector3Interpolator : IBiCubicInterpolator<Vector3>
    {
        public Vector3 BiInterpolate(
            float xBlend, float yBlend,
            Vector3 a00, Vector3 a10, Vector3 a20, Vector3 a30,
            Vector3 a01, Vector3 a11, Vector3 a21, Vector3 a31,
            Vector3 a02, Vector3 a12, Vector3 a22, Vector3 a32,
            Vector3 a03, Vector3 a13, Vector3 a23, Vector3 a33
        )
        {
            return BiCubic.Cubic(
                BiCubic.Cubic(a00, a10, a20, a30, xBlend),
                BiCubic.Cubic(a01, a11, a21, a31, xBlend),
                BiCubic.Cubic(a02, a12, a22, a32, xBlend),
                BiCubic.Cubic(a03, a13, a23, a33, xBlend),
                yBlend
            );
        }
    }
    public readonly struct Vector4Interpolator : IBiCubicInterpolator<Vector4>
    {
        public Vector4 BiInterpolate(
            float xBlend, float yBlend,
            Vector4 a00, Vector4 a10, Vector4 a20, Vector4 a30,
            Vector4 a01, Vector4 a11, Vector4 a21, Vector4 a31,
            Vector4 a02, Vector4 a12, Vector4 a22, Vector4 a32,
            Vector4 a03, Vector4 a13, Vector4 a23, Vector4 a33
        )
        {
            return BiCubic.Cubic(
                BiCubic.Cubic(a00, a10, a20, a30, xBlend),
                BiCubic.Cubic(a01, a11, a21, a31, xBlend),
                BiCubic.Cubic(a02, a12, a22, a32, xBlend),
                BiCubic.Cubic(a03, a13, a23, a33, xBlend),
                yBlend
            );
        }
    }

    public readonly struct BiCubicInterpolatorAdaptor<P, I> : IBiCubicInterpolator<P, P>
        where P : struct
        where I : IBiCubicInterpolator<P>
    {
        public readonly I Interpolator;
        public BiCubicInterpolatorAdaptor(I interpolator) => Interpolator = interpolator;

        public ArrayTextureMap<P> Preprocess(ArrayTextureMap<P> map) => map;
        public P BiInterpolate(float xBlend, float yBlend, P a00, P a10, P a20, P a30, P a01, P a11, P a21, P a31, P a02, P a12, P a22, P a32, P a03, P a13, P a23, P a33)
        {
            return Interpolator.BiInterpolate(
                xBlend, yBlend,
                a00, a10, a20, a30,
                a01, a11, a21, a31,
                a02, a12, a22, a32,
                a03, a13, a23, a33
            );
        }

    }

    public interface IBiCubicInterpolator<P>
        where P : struct
    {
        P BiInterpolate(
            float xBlend, float yBlend,
            P a00, P a10, P a20, P a30,
            P a01, P a11, P a21, P a31,
            P a02, P a12, P a22, P a32,
            P a03, P a13, P a23, P a33
        );
    }
    public interface IBiCubicInterpolator<P, I>
        where P : struct
        where I : struct
    {
        ArrayTextureMap<I> Preprocess(ArrayTextureMap<P> map);
        P BiInterpolate(
            float xBlend, float yBlend,
            I a00, I a10, I a20, I a30,
            I a01, I a11, I a21, I a31,
            I a02, I a12, I a22, I a32,
            I a03, I a13, I a23, I a33
        );
    }


}
