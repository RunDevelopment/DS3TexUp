using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using SixLabors.ImageSharp;
using SoulsFormats;

#nullable enable

namespace DS3TexUpUI
{
    public static class Light
    {
        public static readonly Dictionary<string, (float, float)> CorrectLightAngle = new Dictionary<string, (float, float)>()
        {
            ["m30_00"] = (-0.510f, -0.815f),
            ["m30_01"] = (-0.460f, -2.190f),
            ["m31_00"] = (-0.465f,  0.685f),
            ["m32_00"] = (-0.610f,  1.337f),
            ["m33_00"] = (-0.470f,  0.510f),
            ["m34_01"] = (-0.460f,  2.190f),
            ["m35_00"] = (-0.877f, -3.085f),
            ["m40_00"] = (-0.377f,  1.436f),
            ["m50_00"] = (-0.730f,  2.378f),
        };
    }
}
