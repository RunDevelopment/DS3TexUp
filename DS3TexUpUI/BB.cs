using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using SoulsFormats;
using SixLabors.ImageSharp;
using Pfim;
using SixLabors.ImageSharp.PixelFormats;

#nullable enable

namespace DS3TexUpUI
{
    internal static class BB
    {
        public static IEnumerable<FlverMaterialInfo> ReadAllFlverMaterialInfo()
        {
            foreach (var file in Directory.GetFiles(Data.File(name: @"bb/materials"), "*.json"))
                foreach (var item in file.LoadJsonFile<List<FlverMaterialInfo>>())
                    yield return item;
        }
    }
}
