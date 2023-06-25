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
using System.Numerics;
using System.Text.RegularExpressions;

#nullable enable

namespace DS3TexUpUI
{
    public static class DetailBumps
    {
        private static FLVER2.BufferLayout GetRockBlendBufferLightmapLayout(bool bones)
        {
            if (bones)
            {
                return new FLVER2.BufferLayout()
                {
                    new FLVER.LayoutMember(FLVER.LayoutType.Float3, FLVER.LayoutSemantic.Position, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Short4toFloat4B, FLVER.LayoutSemantic.Normal, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Byte4B, FLVER.LayoutSemantic.Tangent, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Byte4B, FLVER.LayoutSemantic.BoneIndices, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Byte4C, FLVER.LayoutSemantic.BoneWeights, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Float2, FLVER.LayoutSemantic.UV, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Float2, FLVER.LayoutSemantic.UV, 1),
                };
            }
            else
            {
                return new FLVER2.BufferLayout()
                {
                    new FLVER.LayoutMember(FLVER.LayoutType.Float3, FLVER.LayoutSemantic.Position, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Short4toFloat4B, FLVER.LayoutSemantic.Normal, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Byte4B, FLVER.LayoutSemantic.Tangent, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Float2, FLVER.LayoutSemantic.UV, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Float2, FLVER.LayoutSemantic.UV, 1),
                };
            }
        }
        private static FLVER2.BufferLayout GetRockBlendBufferLayout(bool bones)
        {
            if (bones)
            {
                return new FLVER2.BufferLayout()
                {
                    new FLVER.LayoutMember(FLVER.LayoutType.Float3, FLVER.LayoutSemantic.Position, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Short4toFloat4B, FLVER.LayoutSemantic.Normal, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Byte4B, FLVER.LayoutSemantic.Tangent, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Byte4B, FLVER.LayoutSemantic.BoneIndices, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Byte4C, FLVER.LayoutSemantic.BoneWeights, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Float2, FLVER.LayoutSemantic.UV, 0),
                };
            }
            else
            {
                return new FLVER2.BufferLayout()
                {
                    new FLVER.LayoutMember(FLVER.LayoutType.Float3, FLVER.LayoutSemantic.Position, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Short4toFloat4B, FLVER.LayoutSemantic.Normal, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Byte4B, FLVER.LayoutSemantic.Tangent, 0),
                    new FLVER.LayoutMember(FLVER.LayoutType.Float2, FLVER.LayoutSemantic.UV, 0),
                };
            }
        }

        private static FLVER2.GXItem GetRockBlendGXMD(Vector3 reflectance)
        {
            var gxmd = new GXMD();
            gxmd.Items.Add(83, new GXMDItem(value: new Float5(reflectance.X, reflectance.Y, reflectance.Z, 1, 1)));
            gxmd.Items.Add(27, new GXMDItem(value: 2));
            gxmd.Items.Add(28, new GXMDItem(value: 5));
            gxmd.Items.Add(95, new GXMDItem(value: new Vector3(0, 0.5f, 0)));
            return new FLVER2.GXItem(id: "GXMD", unk04: 195, data: gxmd.ToBytes());
        }

        private static void ToRockBlendLightmap(FLVER2.Material material, string bumpPath, Vector2 bumpScale)
        {
            var diffuse = material.GetTex("g_DiffuseTexture");
            var normal = material.GetTex("g_BumpmapTexture");
            var dol1 = material.GetTex("g_DOLTexture1");
            var dol2 = material.GetTex("g_DOLTexture2");

            material.Name += " (detail bump)";
            material.MTD = "N:\\FDP\\data\\Material\\mtd\\map\\Special\\M[RockBlendA]_l.mtd";
            material.Textures = new List<FLVER2.Texture>()
            {
                new FLVER2.Texture(
                    type: "g_DOLTexture1",
                    path: dol1.Path,
                    scale: dol1.Scale,
                    unk10: 1,
                    unk11: true,
                    unk14: 0,
                    unk18: 0,
                    unk1C: 0
                ),
                new FLVER2.Texture(
                    type: "g_DOLTexture2",
                    path: dol2.Path,
                    scale: dol2.Scale,
                    unk10: 1,
                    unk11: true,
                    unk14: 0,
                    unk18: 0,
                    unk1C: 0
                ),
                new FLVER2.Texture(
                    type: "RockBlend_snp_Texture2D_1_GSBlendMap_AlbedoMap_0",
                    path: diffuse.Path,
                    scale: diffuse.Scale,
                    unk10: 1,
                    unk11: true,
                    unk14: 0,
                    unk18: 0,
                    unk1C: 0
                ),
                new FLVER2.Texture(
                    type: "RockBlend_snp_Texture2D_2_GSBlendMap_AlbedoMap_1",
                    path: diffuse.Path,
                    scale: diffuse.Scale,
                    unk10: 1,
                    unk11: true,
                    unk14: 0,
                    unk18: 0,
                    unk1C: 0
                ),
                new FLVER2.Texture(
                    type: "RockBlend_snp_Texture2D_13_GSBlendMap_NormalMap_0",
                    path: normal.Path,
                    scale: normal.Scale,
                    unk10: 1,
                    unk11: true,
                    unk14: 0,
                    unk18: 0,
                    unk1C: 0
                ),
                new FLVER2.Texture(
                    type: "RockBlend_snp_Texture2D_14_GSBlendMap_NormalMap_1",
                    path: bumpPath,
                    scale: bumpScale,
                    unk10: 1,
                    unk11: true,
                    unk14: 0,
                    unk18: 0,
                    unk1C: 0
                ),
            };
        }
        private static void ToRockBlend(FLVER2.Material material, string bumpPath, Vector2 bumpScale)
        {
            var diffuse = material.GetTex("g_DiffuseTexture");
            var normal = material.GetTex("g_BumpmapTexture");

            material.Name += " (detail bump)";
            material.MTD = "N:\\FDP\\data\\Material\\mtd\\map\\Special\\M[RockBlendA].mtd";
            material.Textures = new List<FLVER2.Texture>()
            {
                new FLVER2.Texture(
                    type: "RockBlend_snp_Texture2D_1_GSBlendMap_AlbedoMap_0",
                    path: diffuse.Path,
                    scale: diffuse.Scale,
                    unk10: 1,
                    unk11: true,
                    unk14: 0,
                    unk18: 0,
                    unk1C: 0
                ),
                new FLVER2.Texture(
                    type: "RockBlend_snp_Texture2D_2_GSBlendMap_AlbedoMap_1",
                    path: diffuse.Path,
                    scale: diffuse.Scale,
                    unk10: 1,
                    unk11: true,
                    unk14: 0,
                    unk18: 0,
                    unk1C: 0
                ),
                new FLVER2.Texture(
                    type: "RockBlend_snp_Texture2D_13_GSBlendMap_NormalMap_0",
                    path: normal.Path,
                    scale: normal.Scale,
                    unk10: 1,
                    unk11: true,
                    unk14: 0,
                    unk18: 0,
                    unk1C: 0
                ),
                new FLVER2.Texture(
                    type: "RockBlend_snp_Texture2D_14_GSBlendMap_NormalMap_1",
                    path: bumpPath,
                    scale: bumpScale,
                    unk10: 1,
                    unk11: true,
                    unk14: 0,
                    unk18: 0,
                    unk1C: 0
                ),
            };
        }

        private static void AddGXMD(FLVER2.GXList gxList, FLVER2.GXItem gxmd)
        {
            if (gxmd.ID != "GXMD")
                throw new Exception("Not a GXMD");
            if (gxList.Any(gx => gx.ID == "GXMD"))
                throw new Exception("Already has a GXMD");
            gxList.Insert(0, gxmd);
        }
        private static FLVER2.GXList GetMutableGXList(FLVER2 flver, FLVER2.Material material)
        {
            if (material.GXIndex == -1)
            {
                FLVER2.GXList gxList = new FLVER2.GXList();
                material.GXIndex = flver.GXLists.Count;
                flver.GXLists.Add(gxList);
                return gxList;
            }
            else
            {
                FLVER2.GXList gxList = flver.GXLists[material.GXIndex];
                var othersWithSameIndex = flver.Materials.Any(m => m != material && m.GXIndex == material.GXIndex);
                if (othersWithSameIndex)
                {
                    var copy = new FLVER2.GXList();
                    // deep copy
                    copy.AddRange(gxList.Select(gx => new FLVER2.GXItem(gx.ID, gx.Unk04, gx.Data.ToArray())));
                    gxList = copy;
                    material.GXIndex = flver.GXLists.Count;
                    flver.GXLists.Add(gxList);
                }
                return gxList;
            }
        }
        private static bool TurnOffTessellation(FLVER2.GXList gxList)
        {
            foreach (var gx in gxList)
            {
                if (gx.ID == "GX04" && gx.Unk04 == 100)
                {
                    // Tessellation gx bytes
                    var values = gx.Data.ToGxValues();
                    if (values[1].I != 0)
                    {
                        // turn it off
                        values[1].I = 0;
                        gx.Data = values.ToGxDataBytes();
                        return true;
                    }
                }
            }
            return false;
        }
        private static void SetBufferLayout(FLVER2 flver, int materialIndex, Func<bool, FLVER2.BufferLayout> getLayout)
        {
            var bufferLayoutIndex = -1;
            foreach (var mesh in flver.Meshes)
            {
                if (mesh.MaterialIndex != materialIndex)
                    continue;

                if (mesh.VertexBuffers.Count != 1)
                    throw new Exception("Mesh has more than one vertex buffer!");
                var index = mesh.VertexBuffers[0].LayoutIndex;

                if (bufferLayoutIndex == -1)
                    bufferLayoutIndex = index;
                else if (bufferLayoutIndex != index)
                    throw new Exception("Meshes point to different buffer layouts!");
            }
            if (bufferLayoutIndex == -1)
                throw new Exception("No meshes point to this material!");

            var oldLayout = flver.BufferLayouts[bufferLayoutIndex];
            var hasBones = oldLayout.Any(e => e.Semantic == FLVER.LayoutSemantic.BoneIndices || e.Semantic == FLVER.LayoutSemantic.BoneWeights);

            var othersUseSameLayout = flver.Meshes.Any(m => m.MaterialIndex != materialIndex && m.VertexBuffers.Any(v => v.LayoutIndex == bufferLayoutIndex));
            if (othersUseSameLayout)
            {
                // create a new buffer layout
                var b = new FLVER2.BufferLayout();
                b.AddRange(flver.BufferLayouts[bufferLayoutIndex]);

                var newLayoutIndex = flver.Meshes.SelectMany(m => m.VertexBuffers).Select(v => v.LayoutIndex).Max() + 1;
                if (newLayoutIndex >= flver.BufferLayouts.Count)
                    flver.BufferLayouts.Add(b);
                else
                    flver.BufferLayouts[newLayoutIndex] = b;

                // // change all meshes to use the new layout
                foreach (var mesh in flver.Meshes)
                {
                    if (mesh.MaterialIndex == materialIndex && mesh.VertexBuffers[0].LayoutIndex == bufferLayoutIndex)
                        mesh.VertexBuffers[0].LayoutIndex = newLayoutIndex;
                }

                bufferLayoutIndex = newLayoutIndex;
            }

            // set buffer layouts
            flver.BufferLayouts[bufferLayoutIndex] = getLayout(hasBones);
        }
        public static void AddDetailBump(FLVER2 flver, int materialIndex, string bumpPath, Vector2 bumpScale, Vector3 reflectanceColor)
        {
            // convert material
            var material = flver.Materials[materialIndex];
            var mtdName = Path.GetFileNameWithoutExtension(material.MTD);

            Func<bool, FLVER2.BufferLayout> getBufferLayout;
            int expectedUVs;
            var trimTangents = false;
            var trimUVs = false;
            if (mtdName == "M[ARSN]_l")
            {
                ToRockBlendLightmap(material, bumpPath, bumpScale);
                getBufferLayout = GetRockBlendBufferLightmapLayout;
                expectedUVs = 2;
            }
            else if (mtdName == "M[ARSN]")
            {
                ToRockBlend(material, bumpPath, bumpScale);
                getBufferLayout = GetRockBlendBufferLayout;
                expectedUVs = 1;
            }
            else if (mtdName == "M[ARSN]_Decal")
            {
                ToRockBlend(material, bumpPath, bumpScale);
                getBufferLayout = GetRockBlendBufferLayout;
                expectedUVs = 2;
                trimTangents = true;
            }
            else if (mtdName == "P[ARSN]")
            {
                ToRockBlend(material, bumpPath, bumpScale);
                getBufferLayout = GetRockBlendBufferLayout;
                expectedUVs = 1;
                trimTangents = true;
                trimUVs = true;
            }
            else
                throw new Exception($"Material {mtdName} is not supported");

            // GXIndex
            var gxList = GetMutableGXList(flver, material);
            AddGXMD(gxList, GetRockBlendGXMD(reflectanceColor));
            if (TurnOffTessellation(gxList))
            {
                trimUVs = true;
            }

            // set buffer layouts
            SetBufferLayout(flver, materialIndex, getBufferLayout);

            // convert meshes
            foreach (var mesh in flver.Meshes)
            {
                if (mesh.MaterialIndex != materialIndex)
                    continue;

                // convert vertices
                foreach (var v in mesh.Vertices)
                {
                    if (trimTangents && v.Tangents.Count > 1)
                        v.Tangents.RemoveRange(1, v.Tangents.Count - 1);
                    if (trimUVs && v.UVs.Count > expectedUVs)
                        v.UVs.RemoveRange(expectedUVs, v.UVs.Count - expectedUVs);

                    if (v.Tangents.Count != 1)
                        throw new Exception("Vertex has more than one tangent!");
                    if (v.UVs.Count != expectedUVs)
                        throw new Exception($"Vertex must have {expectedUVs} UVs!");

                    // UV needs to be *2048
                    for (int i = 0; i < v.UVs.Count; i++)
                    {
                        var uv = v.UVs[i] * 2048;
                        uv.Z = 0;
                        v.UVs[i] = uv;
                    }

                    // remove vertex color
                    v.Colors.Clear();
                }
            }
        }

        internal static Vector3 GetReflectanceColor(FLVER2.Texture tex)
        {
            var name = Path.GetFileNameWithoutExtension(tex.Path);

            // ref/base textures
            if (name == "m_ref_nonmetal_r") return new Vector3(60 / 255f);
            if (name == "m_ref_marble_r") return new Vector3(69 / 255f);
            if (name == "m_ref_mud_r") return new Vector3(38 / 255f);

            var texId = TexId.FromTexture(tex);
            if (texId != null)
            {
                // we have a texture
                if (DS3.SolidColorValue.TryGetValue(texId.Value, out var solidColor))
                    return new Vector3(solidColor.R / 255f, solidColor.G / 255f, solidColor.B / 255f);

                throw new Exception($"The reflectance texture {texId} is not a single color!");
            }

            var foo = new Regex(@"^Reflectance_(\d+)_(\d+)_(\d+)_r(?:_l)?$").Match(name);
            if (foo.Success)
            {
                return new Vector3(
                    float.Parse(foo.Groups[1].Value) / 255f,
                    float.Parse(foo.Groups[2].Value) / 255f,
                    float.Parse(foo.Groups[3].Value) / 255f
                );
            }

            throw new Exception($"Unable to parse texture path {tex.Path}!");

        }

        public static FLVER2.Texture GetTex(this FLVER2.Material material, string name)
        {
            var tex = material.Textures.FirstOrDefault(t => t.Type == name);
            if (tex == null)
                throw new Exception($"Material {material.Name} does not have a {name} texture!");
            return tex;
        }
    }

    public class BumpPatch
    {
        public int MaterialIndex { get; set; }
        public string BumpPath { get; set; }
        public Vector2 BumpScale { get; set; }
        public Vector3 Reflectance { get; set; }

        public BumpPatch(int materialIndex, string bumpPath, Vector2 bumpScale, Vector3 reflectance)
        {
            MaterialIndex = materialIndex;
            BumpPath = bumpPath;
            BumpScale = bumpScale;
            Reflectance = reflectance;
        }

        public override string ToString()
        {
            return $"{MaterialIndex} {BumpPath} {BumpScale.X} {BumpScale.X} {Reflectance.X} {Reflectance.Y} {Reflectance.Z}";
        }
    }

    public class BumpConfig
    {
        public Dictionary<string, List<ModelMaterialDef>> Models { get; set; } = new Dictionary<string, List<ModelMaterialDef>>();
        public List<BumpMaterialDef> Materials { get; set; } = new List<BumpMaterialDef>();
        public Dictionary<string, Rgb24> ReflectanceOverwrites { get; set; } = new Dictionary<string, Rgb24>();
        public HashSet<string> IgnoreModels { get; set; } = new HashSet<string>();
        public HashSet<string> IgnoreFlvers { get; set; } = new HashSet<string>();
        public HashSet<string> ExtendConfigs { get; set; } = new HashSet<string>();

        public class ModelMaterialDef
        {
            public int MaterialIndex { get; set; } = 0;
            public string BumpMap { get; set; } = "default";
            public Vector2 Scale { get; set; } = new Vector2(1);
        }

        public void LoadExtend(string thisConfigPath)
        {
            foreach (var config in ExtendConfigs)
            {
                var path = Path.Combine(Path.GetDirectoryName(thisConfigPath)!, config);
                if (!path.EndsWith(".json"))
                    path += ".json";
                var extend = path.LoadJsonFile<BumpConfig>();
                extend.LoadExtend(path);

                // copy materials
                var knownMaterials = Materials.Select(m => (m.Diffuse, m.Normal)).ToHashSet();
                foreach (var material in extend.Materials)
                {
                    if (!knownMaterials.Contains((material.Diffuse, material.Normal)))
                        Materials.Add(material);
                }

                // copy reflectance overwrites
                foreach (var reflectance in extend.ReflectanceOverwrites)
                {
                    if (!ReflectanceOverwrites.ContainsKey(reflectance.Key))
                        ReflectanceOverwrites[reflectance.Key] = reflectance.Value;
                }
            }
        }

        public IEnumerable<BumpPatch> GeneratePatches(FlverMaterialInfo flver, Func<string, string> resolveBumpMap)
        {
            if (flver.FlverPath.EndsWith("_s.flver"))
            {
                // those are low-poly models for shadows
                // we don't want to change their materials
                yield break;
            }
            if (IgnoreFlvers.Contains(Path.GetFileName(flver.FlverPath)))
            {
                yield break;
            }

            if (flver.FlverPath.Contains("001850"))
            {
                int _asded = 8;
            }

            var modelChanges = new Dictionary<int, ModelMaterialDef>();

            var modelId = GetModelName(flver.FlverPath);
            if (modelId != null && IgnoreModels.Contains(modelId))
                yield break;

            if (modelId != null && Models.TryGetValue(modelId, out var models))
                foreach (var model in models)
                    modelChanges[model.MaterialIndex] = model;

            foreach (var (material, materialIndex) in flver.Materials.Select((x, i) => (x, i)))
            {
                if (modelChanges.TryGetValue(materialIndex, out var model))
                {
                    yield return new BumpPatch(materialIndex, resolveBumpMap(model.BumpMap), model.Scale, GetReflectance(material));
                    continue;
                }

                // look for material replacements
                var mtdName = Path.GetFileNameWithoutExtension(material.MTD);
                if (mtdName == "M[ARSN]_l" || mtdName == "M[ARSN]" || mtdName == "M[ARSN]_Decal" || mtdName == "P[ARSN]")
                {
                    var hasGXMD = material.GXIndex != -1 && flver.GXLists[material.GXIndex].Any(gx => gx.ID == "GXMD");
                    if (!hasGXMD)
                    {
                        var diffuse = Path.GetFileNameWithoutExtension(material.GetTex("g_DiffuseTexture").Path);
                        var normal = Path.GetFileNameWithoutExtension(material.GetTex("g_BumpmapTexture").Path);

                        var mat = GetMaterialDef(diffuse, normal);
                        if (mat != null)
                        {
                            Vector3? r = null;
                            try
                            {
                                r = GetReflectance(material);
                            }
                            catch (System.Exception)
                            {
                                if (!mat.IgnoreIncompatible)
                                    throw;
                            }

                            if (r != null)
                                yield return new BumpPatch(materialIndex, resolveBumpMap(mat.BumpMap), mat.Scale, r.Value);

                        }
                    }
                }
            }
        }
        public IEnumerable<BumpPatch> GeneratePatches(FLVER2 flver, string flverPath, Func<string, string> resolveBumpMap)
            => GeneratePatches(FlverMaterialInfo.Of(flver, flverPath), resolveBumpMap);

        public bool Apply(FLVER2 flver, string flverPath, Func<string, string> resolveBumpMap)
        {
            var changed = false;
            foreach (var patch in GeneratePatches(flver, flverPath, resolveBumpMap))
            {
                DetailBumps.AddDetailBump(flver, patch.MaterialIndex, patch.BumpPath, patch.BumpScale, patch.Reflectance);
                changed = true;
            }
            return changed;
        }
        private static string? GetModelName(string flverPath)
        {
            var name = Path.GetFileNameWithoutExtension(flverPath);

            if (name.StartsWith("m"))
            {
                var id = name.Substring("m40_00_00_00_".Length);
                if (id.Length != 6)
                    throw new Exception($"{name} is not a valid map part flver name.");
                return "m" + id;
            }
            if (name.StartsWith("o"))
            {
                var id = name;
                if (id.EndsWith("_1") || id.EndsWith("_s")) id = id.Substring(0, id.Length - 2);

                if (id.Length != 7)
                    throw new Exception($"{id} is not a valid map part flver name.");
                return id;
            }

            return null;
        }
        public BumpMaterialDef? GetMaterialDef(string diffuse, string normal)
        {
            foreach (var mat in Materials)
            {
                if (mat.Diffuse == diffuse && mat.Normal == normal)
                    return mat;
            }
            return null;
        }
        public Vector3 GetReflectance(FLVER2.Material material)
        {
            var refl = material.GetTex("g_SpecularTexture");

            var name = Path.GetFileNameWithoutExtension(refl.Path);
            if (ReflectanceOverwrites.TryGetValue(name, out var color))
                return new Vector3(color.R / 255f, color.G / 255f, color.B / 255f);

            return DetailBumps.GetReflectanceColor(refl);
        }
    }
    public class BumpMaterialDef
    {
        public string Diffuse { get; set; }
        public string Normal { get; set; }
        public bool IgnoreIncompatible { get; set; } = false;
        public string BumpMap { get; set; } = "default";
        public Vector2 Scale { get; set; } = new Vector2(8);

        public BumpMaterialDef(string diffuse, string normal)
        {
            Diffuse = diffuse;
            Normal = normal;
        }
    }
    public class BumpMapsStore
    {
        public Dictionary<string, Dictionary<string, string>> BumpMaps { get; set; }
        public BumpMapsStore(Dictionary<string, Dictionary<string, string>> bumpMaps)
        {
            BumpMaps = bumpMaps;
        }

        public string GetBumpMap(string bumpName, string map)
        {
            if (!bumpName.Contains('/'))
                bumpName = bumpName + "/" + map;

            return GetBumpMap(bumpName);
        }
        public string GetBumpMap(string bump)
        {
            var slash = bump.IndexOf('/');
            if (slash == -1)
                throw new Exception($"Bump map {bump} is not a qualified name.");

            var name = bump.Substring(0, slash);
            var map = bump.Substring(slash + 1);

            if (BumpMaps.TryGetValue(name, out var mapDict))
            {
                if (mapDict.TryGetValue(map, out var texture))
                    return texture;
                throw new Exception($"The bump map {name} is not available in {map}.");
            }
            throw new Exception($"There is no bump map with the name {name}.");
        }
    }
}
