using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Linq;
using SoulsFormats;

#nullable enable

namespace DS3TexUpUI
{
    public static class Light
    {
        private static T LoadFile<T>(string file) => Data.File(@$"gparam/{file}", Data.Source.Local).LoadJsonFile<T>();

        /// Some light (id=0) have a wrong Angle0 value. Those are the correct values.
        public static Dictionary<string, (float, float)> GetCorrectedLightAngles()
            => LoadFile<Dictionary<string, (float, float)>>(@"corrected-light-angles.json");
        public static HashSet<string> GetFilesWithLight()
            => LoadFile<HashSet<string>>(@"with-light.json");
        public static HashSet<string> GetBuggedShadowParam()
            => LoadFile<HashSet<string>>(@"bugged-shadow-param.json");

        private static string GetMapPieceId(string path)
        {
            var name = Path.GetFileName(path);
            if (name.Length < "m??_??".Length || name[0] != 'm' || name[3] != '_')
            {
                throw new ArgumentException("Invalid map piece id: " + name);
            }
            return name.Substring(0, "m??_??".Length);
        }

        internal static void CreateModdedDrawParams()
        {
            const string CleanDrawParam = @"C:\Users\micha\Desktop\gparam\original";
            const string TargetDrawParam = @"C:\Users\micha\Desktop\gparam\modded";

            var files = Directory
                .GetFiles(CleanDrawParam, "m??_??_????.gparam.dcx")
                .Where(f => DS3.MapPieces.Contains(GetMapPieceId(f)))
                .ToArray();

            var filesWithLight = GetFilesWithLight();
            var correctedAngles = GetCorrectedLightAngles();
            var buggedShadowParam = GetBuggedShadowParam();

            foreach (var file in files)
            {
                var name = Path.GetFileName(file).Substring(0, "m??_??_????".Length);

                var gparam = GPARAM.Read(file);
                var changed = false;

                var lightAngle = gparam.GetGroup("LightSet ParamEditor")?.GetParam("Directional Light Angle0");
                var hasMainLight = lightAngle != null && lightAngle.ValueIDs.Count > 0 && lightAngle.ValueIDs[0] == 0;

                // correct light angle
                if (correctedAngles.TryGetValue(GetMapPieceId(file), out var correctAngle))
                {
                    if (lightAngle != null && hasMainLight)
                    {
                        lightAngle.Values[0] = new Vector2(correctAngle.Item1, correctAngle.Item2);
                        changed = true;
                    }
                }

                // change shadow params
                var shadowGroup = gparam.GetGroup("ShadowParam ParamEditor");
                if (
                    true
                    && !buggedShadowParam.Contains(name)
                    && shadowGroup != null
                    && shadowGroup.Params[0].ValueIDs.Count == 0
                )
                {
                    shadowGroup.AddParams(0, new Dictionary<string, object>()
                    {
                        ["Angle"] = new Vector2(0, 0),
                        ["Shadow  Light  Id"] = (int)0,
                        ["Near"] = (float)0.1f,
                        ["Cascade Dist 0->1"] = (float)3f,
                        ["Cascade Dist 1->2"] = (float)12f,
                        ["Cascade Dist 2->3"] = (float)20f,
                        ["Far Fade Start"] = (float)50f,
                        ["Far Fade Dist"] = (float)25f,
                        ["Shadow Map FilterMode"] = (int)2, // 0=PCF4 1=PCF9 2=SOFT
                        ["Depth Offset"] = (float)-0.015f,
                        ["Volume Depth"] = (float)100f,
                        ["Slope Scaled Depth Bias"] = (float)0.005f,
                        ["Shadow Model Cull Flip"] = false,
                        ["AlignTexel"] = true,
                        ["DepthRate"] = (float)0.1f,
                        ["DepthScale"] = (float)10f,
                        ["KernelScale"] = (float)1f,
                        ["DepthOffsetScale"] = (float)0f,
                        ["Blur Count"] = (int)1,
                        ["Blur Radius"] = (float)2f,
                        // ["Shadow Color"] = new byte[] { 10, 30, 50, 128 }, // BGRA color (byte4)
                        ["Shadow Map Resolution"] = (int)3, // (doesn't work) 0=2048x2048(Default) 1=1024x1024 2=2048x2048 3=4096x4096
                    });
                    changed = true;
                }

                if (changed)
                {
                    var targetFile = Path.Join(TargetDrawParam, Path.GetFileName(file));
                    gparam.Write(targetFile);
                }
            }
        }

        public static GPARAM.Group? GetGroup(this GPARAM gparam, string? name1 = null, string? name2 = null)
        {
            if (name1 != null) return gparam.Groups.FirstOrDefault(p => p.Name1 == name1);
            return gparam.Groups.FirstOrDefault(p => p.Name2 == name2);
        }
        public static GPARAM.Param? GetParam(this GPARAM.Group group, string? name1 = null, string? name2 = null)
        {
            if (name1 != null) return group.Params.FirstOrDefault(p => p.Name1 == name1);
            return group.Params.FirstOrDefault(p => p.Name2 == name2);
        }
        public static void AddParams(this GPARAM.Group group, int id, Dictionary<string, object> values)
        {
            foreach (var param in group.Params)
            {
                if (param.ValueIDs.Contains(id))
                    throw new Exception($"Id {id} already exists in group '{group.Name1}'");

                if (!values.TryGetValue(param.Name2, out var value))
                    continue;
                // throw new Exception($"No value specified for param '{param.Name2}' in group '{group.Name1}'");

                param.ValueIDs.Add(id);
                param.Values.Add(value);
            }
        }
    }
}
