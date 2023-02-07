using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Linq;
using System.Text.Json;
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
        public static Dictionary<string, Dictionary<string, object>> GetShadowParamOverrides()
            => LoadFile<Dictionary<string, Dictionary<string, object>>>(@"shadow-param-overrides.json");

        private static string GetMapPieceId(string path)
        {
            var name = Path.GetFileName(path);
            if (name.Length < "m??_??".Length || name[0] != 'm' || name[3] != '_')
            {
                throw new ArgumentException("Invalid map piece id: " + name);
            }
            return name.Substring(0, "m??_??".Length);
        }

        private static Dictionary<string, object> GetShadowParams(IReadOnlyDictionary<string, object> overrides)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();

            void Add<T>(string name, T defaultValue)
                where T : notnull
            {
                T value = defaultValue;
                if (overrides.TryGetValue(name, out var over))
                {
                    if (over is JsonElement json)
                    {
                        value = JsonConverters.Deserialize<T>(json);
                    }
                    else
                    {
                        value = (T)over;
                    }
                }
                result[name] = value;
            }
            void AddF(string name, float defaultValue) => Add<float>(name, defaultValue);
            void AddI(string name, int defaultValue) => Add<int>(name, defaultValue);

            Add<Vector2>("Angle", new Vector2(0, 0));
            AddI("Shadow  Light  Id", 0);
            AddF("Near", 0.1f);
            AddF("Cascade Dist 0->1", 3.5f);
            AddF("Cascade Dist 1->2", 14f);
            AddF("Cascade Dist 2->3", 30f);
            AddF("Far Fade Start", 60f);
            AddF("Far Fade Dist", 25f);
            AddI("Shadow Map FilterMode", 2); // 0=PCF4 1=PCF9 2=SOFT
            AddF("Depth Offset", -0.015f);
            AddF("Volume Depth", 100f);
            AddF("Slope Scaled Depth Bias", 0.005f);
            Add<bool>("Shadow Model Cull Flip", false);
            Add<bool>("AlignTexel", true);
            AddF("DepthRate", 0.1f);
            AddF("DepthScale", 5f);
            AddF("KernelScale", 1f);
            AddF("DepthOffsetScale", 0f);
            AddI("Blur Count", 1);
            AddF("Blur Radius", 1f);
            // Add("Shadow Color", new byte[] { 10, 30, 50, 128 }); // BGRA color (byte4)
            AddI("Shadow Map Resolution", 3); // (doesn't work) 0=2048x2048(Default) 1=1024x1024 2=2048x2048 3=4096x4096

            return result;
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
            var shadowParamOverrides = GetShadowParamOverrides();

            foreach (var file in files)
            {
                var name = Path.GetFileName(file).Substring(0, "m??_??_????".Length);
                if (name.StartsWith("m51"))
                {
                    int asdas = 0;
                }

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
                    shadowGroup.AddParams(0, GetShadowParams(shadowParamOverrides.GetOrNew(GetMapPieceId(file))));
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
