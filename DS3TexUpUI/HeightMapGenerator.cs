using System;
using System.Numerics;

namespace DS3TexUpUI
{
    public static class HeightMapGenerator
    {
        private static Slope[] ToSlopes(DS3NormalMap.NormalView normals)
        {
            var count = normals.Count;
            var slopes = new Slope[count];
            for (int i = 0; i < count; i++)
                slopes[i] = (Slope)normals[i];
            return slopes;
        }

        public static ArrayTextureMap<float> Gen1<M>(M map)
            where M : ITextureMap<Normal>
        {
            var slopes = map.Convert(Slope.FromNormal);
            var p = Poly2.FromSlopes(slopes);

            var (w, h) = (map.Width, map.Height);
            var heights = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    heights[y * w + x] = p[(x + .5f) / w, (y + .5f) / h];
                }
            }

            var result = heights.AsTextureMap(w);
            result.Normalize();
            return result;
        }
    }

    public readonly struct Slope
    {
        public readonly float dx;
        public readonly float dy;

        public Slope(float dx, float dy)
        {
            this.dx = dx;
            this.dy = dy;
        }

        public static Slope FromNormal(Normal n)
        {
            var f = -1f / (n.Z < 0.001f ? 0.001f : n.Z);
            return new Slope(n.X * f, n.Y * f);
        }

        public Normal ToNormal()
        {
            var a = new Vector3(1f, 0f, dx);
            var b = new Vector3(0f, 1f, dy);
            return Normal.FromVector(Vector3.Cross(a, b));
        }

        public static implicit operator Normal(Slope s) => s.ToNormal();
        public static explicit operator Slope(Normal n) => FromNormal(n);
    }
}
