using System;
using MathNet.Numerics.LinearAlgebra.Single;
using MathNet.Numerics.LinearRegression;

namespace DS3TexUpUI
{
    public class Poly2
    {
        public readonly float[] Coefficients;
        public readonly int DegreeX;
        public readonly int DegreeY;

        public float this[float x, float y]
        {
            get
            {
                var sum = 0f;

                var _y = 1f;
                for (int stride = 0; stride < Coefficients.Length; stride += DegreeY + 1)
                {
                    var _x = 1f;
                    for (int i = 0; i <= DegreeX; i++)
                    {
                        sum += Coefficients[stride + i] * _x * _y;
                        _x *= x;
                    }
                    _y *= y;
                }

                return sum;
            }
        }

        public Poly2(int degreeX, int degreeY)
        {
            DegreeX = degreeX;
            DegreeY = degreeY;
            Coefficients = new float[(degreeX + 1) * (degreeY + 1)];
        }
        public Poly2(Poly2 other)
        {
            DegreeX = other.DegreeX;
            DegreeY = other.DegreeY;
            Coefficients = new float[other.Coefficients.Length];
            Array.Copy(other.Coefficients, Coefficients, Coefficients.Length);
        }

        /// <summary>
        /// <para>This will construct a 2D polynomial from the given map of slopes.</para>
        ///
        /// <para>It will use O((Width * Height) ** 2) memory and O((Width * Height) ** 3) time.</para>
        ///
        /// <para>The discrete indexes will be used to inputs with an 0.5 offset.
        /// E.g. index (i,j) will used as (x,y)=((i+.5)/Width,(j+.5)/Height).</para>
        /// </summary>
        public static Poly2 FromSlopes<M>(M slopes, float c00 = 0f)
            where M : ITextureMap<Slope>
        {
            static void SetPartialDerivativeX(float[] values, int start, int step, int degreeX, int degreeY, float x, float y)
            {
                var index = start;

                for (int j = 0; j <= degreeY; j++)
                {
                    for (int i = 0; i <= degreeX; i++)
                    {
                        if (i == 0 && j == 0) continue;

                        var v = i * MathF.Pow(x, i - 1) * MathF.Pow(y, j);
                        if (!float.IsFinite(v))
                        {
                            var i8 = 0;
                        }
                        values[index] = v;
                        index += step;
                    }
                }
            }
            static void SetPartialDerivativeY(float[] values, int start, int step, int degreeX, int degreeY, float x, float y)
            {
                var index = start;

                for (int j = 0; j <= degreeY; j++)
                {
                    for (int i = 0; i <= degreeX; i++)
                    {
                        if (i == 0 && j == 0) continue;

                        values[index] = j * MathF.Pow(x, i) * MathF.Pow(y, j - 1);
                        index += step;
                    }
                }
            }

            var count = slopes.Count;
            var (w, h) = (slopes.Width, slopes.Height);
            var (degreeX, degreeY) = (w, h);
            var coefficients = (w + 1) * (h + 1);

            var (rows, columns) = (count * 2, coefficients - 1);
            var m = new DenseMatrix(rows, columns);
            var mValues = m.Values;

            var v = new DenseVector(rows);
            var vValues = v.Values;

            for (var y = 0; y < h; y++)
            {
                var yf = (y + .5f) / h;
                for (var x = 0; x < w; x++)
                {
                    var xf = (x + .5f) / w;

                    var index = y * w + x;
                    var slope = slopes[index];

                    var i1 = index * 2;
                    var i2 = index * 2 + 1;

                    SetPartialDerivativeX(mValues, i1, rows, degreeX, degreeY, xf, yf);
                    SetPartialDerivativeY(mValues, i2, rows, degreeX, degreeY, xf, yf);

                    vValues[i1] = slope.dx;
                    vValues[i2] = slope.dy;
                }
            }

            var c = MultipleRegression.NormalEquations(m, v);

            var polynomial = new Poly2(degreeX, degreeY);
            polynomial.Coefficients[0] = c00;
            for (int i = 1; i < coefficients; i++)
                polynomial.Coefficients[i] = c[i - 1];

            return polynomial;
        }
    }
}
