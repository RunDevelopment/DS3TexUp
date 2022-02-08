using System;
using System.Numerics;

namespace DS3TexUpUI
{
    public static class HeightMapGenerator
    {
        public static ArrayTextureMap<Slope> ToSlopes(this DS3NormalMap.NormalView normals)
        {
            var count = normals.Count;
            var slopes = new Slope[count];
            for (int i = 0; i < count; i++)
                slopes[i] = (Slope)normals[i];
            return slopes.AsTextureMap(normals.Width);
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

        public static ArrayTextureMap<float> Gen2(this DS3NormalMap.NormalView normals)
        {
            var slopes = ToSlopes(normals);

            // var heights = GenerateHeightMapCross(slopes, normals.Width / 2, normals.Height / 2);
            var heights = GenerateHeightMapCrossTopLeft(slopes);

            heights.Normalize();
            return heights;
        }
        public static ArrayTextureMap<float> GenerateHeightMapCrossTopLeft(ArrayTextureMap<Slope> slopes)
        {
            static float GetDX(ArrayTextureMap<Slope> slopes, int y, int x1, int x2)
            {
                var stride = y * slopes.Width;
                return (slopes[stride + x1].dx + slopes[stride + x2].dx) * 0.5f;
            }
            static float GetDY(ArrayTextureMap<Slope> slopes, int x, int y1, int y2)
            {
                return (slopes[y1 * slopes.Width + x].dy + slopes[y2 * slopes.Width + x].dy) * 0.5f;
            }

            var w = slopes.Width;
            var h = slopes.Height;

            var result = new float[slopes.Count].AsTextureMap(slopes.Width);

            // Draw the cross
            for (var x = 1; x < w; x++)
                result[x, 0] = result[x - 1, 0] + GetDX(slopes, 0, x - 1, x);
            for (var y = 0 + 1; y < h; y++)
                result[0, y] = result[0, y - 1] + GetDY(slopes, 0, y - 1, y);

            // Fill area
            for (int y = 1; y < h; y++)
            {
                for (int x = 1; x < w; x++)
                {
                    var hx = result[x - 1, y] + GetDX(slopes, y, x - 1, x);
                    var hy = result[x, y - 1] + GetDY(slopes, x, y - 1, y);
                    result[x, y] = (hx + hy) * .5f;
                }
            }

            return result;
        }
        public static ArrayTextureMap<float> GenerateHeightMapCross(ArrayTextureMap<Slope> slopes, int startX, int startY)
        {
            static float GetDX(ArrayTextureMap<Slope> slopes, int y, int x1, int x2)
            {
                var stride = y * slopes.Width;
                return (slopes[stride + x1].dx + slopes[stride + x2].dx) * 0.5f;
            }
            static float GetDY(ArrayTextureMap<Slope> slopes, int x, int y1, int y2)
            {
                return (slopes[y1 * slopes.Width + x].dy + slopes[y2 * slopes.Width + x].dy) * 0.5f;
            }

            var w = slopes.Width;
            var h = slopes.Height;

            var result = new float[slopes.Count].AsTextureMap(slopes.Width);

            // Draw the cross
            for (var x = startX + 1; x < w; x++)
                result[x, startY] = result[x - 1, startY] + GetDX(slopes, startY, x - 1, x);
            for (var x = startX - 1; x >= 0; x--)
                result[x, startY] = result[x + 1, startY] + GetDX(slopes, startY, x + 1, x);
            for (var y = startY + 1; y < h; y++)
                result[startX, y] = result[startX, y - 1] + GetDY(slopes, startX, y - 1, y);
            for (var y = startY - 1; y >= 0; y--)
                result[startX, y] = result[startX, y + 1] + GetDY(slopes, startX, y + 1, y);

            // TODO:

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
            return new Slope(-n.X * f, n.Y * f);
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
