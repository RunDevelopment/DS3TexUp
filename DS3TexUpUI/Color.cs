using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DS3TexUpUI
{
    public struct YCbCr
    {
        public byte Y;
        public byte Cb;
        public byte Cr;

        public YCbCr(byte y, byte cb, byte cr)
        {
            Y = y;
            Cb = cb;
            Cr = cr;
        }

        public static YCbCr FromRgb(byte r, byte g, byte b)
        {
            return new YCbCr(
                (byte)Math.Clamp((int)(0f + 0.299f * r + 0.587f * g + 0.114f * b), 0, 255),
                (byte)Math.Clamp((int)(128f - 0.168736f * r + 0.331264f * g + 0.5f * b), 0, 255),
                (byte)Math.Clamp((int)(128f + 0.5f * r + 0.418688f * g + 0.081312f * b), 0, 255)
            );
        }
        public static YCbCr FromRgb(Rgb24 color) => FromRgb(color.R, color.G, color.B);
        public static YCbCr FromRgb(Rgba32 color) => FromRgb(color.R, color.G, color.B);

        public Rgb24 ToRgb()
        {
            return new Rgb24(
                (byte)Math.Clamp((int)(Y + 1.402f * (Cr - 128)), 0, 255),
                (byte)Math.Clamp((int)(Y - 0.344136f * (Cb - 128) - 0.714136f * (Cr - 128)), 0, 255),
                (byte)Math.Clamp((int)(Y + 1.772f * (Cb - 128)), 0, 255)
            );
        }

        public void Deconstruct(out byte y, out byte cb, out byte cr)
        {
            y = Y;
            cb = Cb;
            cr = Cr;
        }

        public static explicit operator YCbCr(Rgba32 c) => FromRgb(c);
        public static implicit operator YCbCr(Rgb24 c) => FromRgb(c);
        public static implicit operator Rgb24(YCbCr c) => c.ToRgb();
    }

    public struct HSV
    {
        // [0, 360)
        public float H;
        // [0, 1]
        public float S;
        // [0, 1]
        public float V;

        public HSV(float h, float s, float v)
        {
            H = h;
            S = s;
            V = v;
        }

        public static HSV FromRgb(byte r, byte g, byte b)
        {
            var min = Math.Min(r, Math.Min(g, b));
            var max = Math.Max(r, Math.Max(g, b));
            var c = max - min;

            float h;
            if (c == 0)
                h = 0;
            else if (max == r)
                h = (g - b) / (float)c % 6;
            else if (max == g)
                h = (b - r) / (float)c + 2;
            else
                h = (r - g) / (float)c + 4;

            float s = max == 0 ? 0 : c / (float)max;

            return new HSV(h * 60, s, max / 255f);
        }
        public static HSV FromRgb(Rgb24 color) => FromRgb(color.R, color.G, color.B);
        public static HSV FromRgb(Rgba32 color) => FromRgb(color.R, color.G, color.B);

        public Rgb24 ToRgb()
        {
            return new Rgb24(ToRgbComponent(5), ToRgbComponent(3), ToRgbComponent(1));
        }
        private byte ToRgbComponent(float n)
        {
            var k = (n + H / 60) % 6;
            var f = V - V * S * Math.Clamp(MathF.Min(k, 4 - k), 0f, 1f);
            return (byte)(Math.Clamp(f, 0f, 1f) * 255);
        }

        public void Deconstruct(out float h, out float s, out float v)
        {
            h = H;
            s = S;
            v = V;
        }

        public static explicit operator HSV(Rgba32 c) => FromRgb(c);
        public static implicit operator HSV(Rgb24 c) => FromRgb(c);
        public static implicit operator Rgb24(HSV c) => c.ToRgb();
    }

    public static class ColorExtensions
    {
        public static Rgb24 Lerp(this Rgb24 c0, Rgb24 c1, float blend)
        {
            return new Rgb24(
                (byte)(c0.R * (1 - blend) + c1.R * blend),
                (byte)(c0.G * (1 - blend) + c1.G * blend),
                (byte)(c0.B * (1 - blend) + c1.B * blend)
            );
        }
        public static Rgba32 Lerp(this Rgba32 c0, Rgba32 c1, float blend)
        {
            return new Rgba32(
                (byte)(c0.R * (1 - blend) + c1.R * blend),
                (byte)(c0.G * (1 - blend) + c1.G * blend),
                (byte)(c0.B * (1 - blend) + c1.B * blend),
                (byte)(c0.A * (1 - blend) + c1.A * blend)
            );
        }

        // The byte will be the average of the RGB channels.
        public static byte GetGreyAverage(this Rgba32 c)
        {
            return (byte)((c.R + c.B + c.G) / 3);
        }
        // The byte will be the perceived brightness of the color.
        public static byte GetGreyBrightness(this Rgba32 c)
        {
            return (byte)((c.R * 3 + c.B * 11 + c.G * 2) / 16);
        }
        // The byte will be a linear interpolation between the min and max of all channels. The blend factor will be
        // avg(rgb) / 255.
        public static byte GetGreyMinMaxBlend(this Rgba32 c)
        {
            var min = Math.Min(c.R, Math.Min(c.G, c.B));
            var max = Math.Max(c.R, Math.Max(c.G, c.B));
            var avg = c.GetGreyAverage();
            var blend = (byte)((max * avg + min * (255 - avg)) / 255);
            return blend;
        }
    }
}
