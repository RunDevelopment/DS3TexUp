using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Numerics;
using System.Reflection;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SoulsFormats;

#nullable enable

namespace DS3TexUpUI
{
    class MaterialMasks
    {
        public static ArrayTextureMap<float> DetectGold(ArrayTextureMap<Rgba32> reflective)
        {
            static float GetValue(float v, float lowOut, float lowIn, float highIn, float highOut)
            {
                if (v <= lowOut) return 0;
                if (v < lowIn) return (v - lowOut) / (lowIn - lowOut);
                if (v <= highIn) return 1;
                if (v < highOut) return 1 - (v - highIn) / (highOut - highIn);
                return 0;
            }

            ArrayTextureMap<float> DetectGoldCertain()
            {
                var result = new float[reflective.Count];
                for (int i = 0; i < reflective.Data.Length; i++)
                {
                    HSV c = reflective.Data[i].Rgb;
                    result[i] =
                        GetValue(c.H, 28, 35, 51, 58)
                        * GetValue(c.S, 0.26f, 0.30f, 0.55f, 0.65f)
                        * GetValue(c.V, 0.30f, 0.50f, 1, 1);
                }
                return result.AsTextureMap(reflective.Width);
            }
            ArrayTextureMap<float> DetectGoldUncertain()
            {
                var result = new float[reflective.Count];
                for (int i = 0; i < reflective.Data.Length; i++)
                {
                    HSV c = reflective.Data[i].Rgb;
                    result[i] =
                        GetValue(c.H, 26, 31, 55, 60)
                        * GetValue(c.S, 0.15f, 0.26f, 0.60f, 0.70f)
                        * GetValue(c.V, 0.30f, 0.50f, 1, 1);
                }
                return result.AsTextureMap(reflective.Width);
            }

            var certain = DetectGoldCertain();
            var certainBlur = certain.Blur(4, MaxAcc.Float);
            var uncertain = DetectGoldUncertain();

            var result = new float[reflective.Count];
            for (int i = 0; i < certain.Data.Length; i++)
            {
                var c = certain.Data[i];
                var cb = certainBlur.Data[i];
                var u = uncertain.Data[i];
                result[i] = Math.Max(c, Math.Min(cb, u));
            }
            return result.AsTextureMap(reflective.Width);
        }
    }
}
