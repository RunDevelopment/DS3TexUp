using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Numerics;
using System.Linq;
using SoulsFormats;

namespace DS3TexUpUI
{
    struct FlverMaterialInfo
    {
        public string FlverPath { get; set; }
        public List<FLVER2.GXList> GXLists { get; set; }
        public List<FLVER2.Material> Materials { get; set; }

        public static readonly IReadOnlyList<JsonConverter> Converters = new JsonConverter[] {
            new Vector2Converter()
        };

        public static IEnumerable<FlverMaterialInfo> ReadAll()
        {
            var dir = Path.Join(AppDomain.CurrentDomain.BaseDirectory, @"data\materials");
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                var jr = new Utf8JsonReader(File.ReadAllBytes(file));

                var options = new JsonSerializerOptions();
                foreach (var c in FlverMaterialInfo.Converters)
                    options.Converters.Add(c);

                var list = JsonSerializer.Deserialize<List<FlverMaterialInfo>>(ref jr, options);
                foreach (var item in list)
                    yield return item;
            }
        }
    }

    sealed class Vector2Converter : JsonConverter<Vector2>
    {
        public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();

            var x = 0f;
            var y = 0f;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) return new Vector2(x, y);

                if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();

                var name = reader.GetString();
                reader.Read();

                switch (name)
                {
                    case "X":
                        x = reader.GetSingle();
                        break;
                    case "Y":
                        y = reader.GetSingle();
                        break;
                    default:
                        throw new JsonException();
                }
            }

            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("X", value.X);
            writer.WriteNumber("Y", value.Y);
            writer.WriteEndObject();
        }
    }

    public static class MaterialExtensions
    {
        private static readonly Dictionary<string, TexKind> _textureTypeMap = new Dictionary<string, TexKind>()
        {
            // Albedo
            ["g_DiffuseTexture"] = TexKind.Albedo,
            ["SAT_Equip_snp_Texture2D_5_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend2_et1_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["g_DiffuseTexture2"] = TexKind.Albedo,
            ["MultiBlend2_et1_snp_Texture2D_2_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["MultiBlend3_et1_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend3_et1_snp_Texture2D_2_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["MultiBlend3_et1_snp_Texture2D_3_GSBlendMap_AlbedoMap_2"] = TexKind.Albedo,
            ["MultiBlend4_et1_snp_Texture2D_2_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["MultiBlend4_et1_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend4_et1_snp_Texture2D_3_GSBlendMap_AlbedoMap_2"] = TexKind.Albedo,
            ["C_ARSN_cinder_snp_Texture2D_5_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend4_et1_snp_Texture2D_4_GSBlendMap_AlbedoMap_3"] = TexKind.Albedo,
            ["C_ARSN_cinder_em_snp_Texture2D_5_AlbedoMap_0"] = TexKind.Albedo,
            ["RockBlend_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend2_et1_nolight_MuitiBlendMask_snp_Texture2D_2_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["Fujir_Nolight_OpacityBlend_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend2_et1_nolight_MuitiBlendMask_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["SAT_Equip_em_snp_Texture2D_5_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend2_et1_em_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend2_et1_em_snp_Texture2D_2_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["RockBlend_snp_Texture2D_2_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["MultiBlend2_et1_e_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend3_et1_e_snp_Texture2D_3_GSBlendMap_AlbedoMap_2"] = TexKind.Albedo,
            ["MultiBlend3_et1_e_snp_Texture2D_2_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["MultiBlend3_et1_e_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["Fur_FDP_snp_Texture2D_1_AlbedoMap_0"] = TexKind.Albedo,
            ["Fur_FDP_snp_Texture2D_2_AlbedoMap_1"] = TexKind.Albedo,
            ["C_ARSN_em_snp_Texture2D_5_AlbedoMap_0"] = TexKind.Albedo,
            ["Veill_snp_Texture2D_2_AlbedoMap_0"] = TexKind.Albedo,
            ["SAT_Equip_SSS_snp_Texture2D_5_AlbedoMap_0"] = TexKind.Albedo,
            ["SAT_Equip_Veill_snp_Texture2D_5_AlbedoMap_0"] = TexKind.Albedo,
            ["RecursiveCrystal_snp_Texture2D_2_AlbedoMap_0"] = TexKind.Albedo,
            ["SAT_MultiBlend2_BlendOpacity_Equip_Cm_snp_Texture2D_2_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["SAT_MultiBlend2_BlendOpacity_Equip_Cm_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["FlowmapVertexOffset_snp_Texture2D_6_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend4_et1_e_snp_Texture2D_4_GSBlendMap_AlbedoMap_3"] = TexKind.Albedo,
            ["MultiBlend2_et1_e_snp_Texture2D_2_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["SandBlend_2_snp_Texture2D_8_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["BloodBlend_snp_Texture2D_2_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["Veill_NoLight_snp_Texture2D_2_AlbedoMap_0"] = TexKind.Albedo,
            ["SAT_Equip_Df_snp_Texture2D_6_AlbedoMap_1"] = TexKind.Albedo,
            ["Rotten_snp_Texture2D_3_AlbedoMap_0"] = TexKind.Albedo,
            ["Rotten_snp_Texture2D_7_AlbedoMap_1"] = TexKind.Albedo,
            ["SAT_Equip_Df_snp_Texture2D_5_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend2_et1_nolight_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["BurnCloth_snp_Texture2D_5_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend2_et1_nolight_snp_Texture2D_2_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["SandBlend_2_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["SandBlend_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["BloodBlend_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend2_et1_em_Mask_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend2_et1_mt1_snp_Texture2D_17_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend2_et1_mt1_snp_Texture2D_16_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["MultiBlend3_et1_mp1_snp_Texture2D_17_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend2_et1_p_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend2_et1_p_snp_Texture2D_2_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["MultiBlend4_et1_mt1_snp_Texture2D_17_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend4_et1_mt1_snp_Texture2D_16_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["MultiBlend4_et1_mt1_snp_Texture2D_15_GSBlendMap_AlbedoMap_2"] = TexKind.Albedo,
            ["MultiBlend4_et1_mt1_snp_Texture2D_14_GSBlendMap_AlbedoMap_3"] = TexKind.Albedo,
            ["MultiBlend3_et1_mp1_snp_Texture2D_16_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["MultiBlend3_et1_mp1_snp_Texture2D_15_GSBlendMap_AlbedoMap_2"] = TexKind.Albedo,
            ["MultiBlend2_et1_em_Mask2_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend2_et1_em_Mask2_snp_Texture2D_2_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["FlowmapVertexOffset_em_snp_Texture2D_6_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend4_et1_e_snp_Texture2D_1_GSBlendMap_AlbedoMap_0"] = TexKind.Albedo,
            ["MultiBlend4_et1_e_snp_Texture2D_2_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,
            ["MultiBlend4_et1_e_snp_Texture2D_3_GSBlendMap_AlbedoMap_2"] = TexKind.Albedo,
            ["MultiBlend2_et1_em_Mask_snp_Texture2D_2_GSBlendMap_AlbedoMap_1"] = TexKind.Albedo,

            // Normal
            ["g_BumpmapTexture"] = TexKind.Normal,
            ["SAT_Equip_snp_Texture2D_4_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend2_et1_snp_Texture2D_13_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend2_et1_snp_Texture2D_14_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["g_BumpmapTexture2"] = TexKind.Normal,
            ["MultiBlend3_et1_snp_Texture2D_14_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["MultiBlend3_et1_snp_Texture2D_13_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend3_et1_snp_Texture2D_15_GSBlendMap_NormalMap_2"] = TexKind.Normal,
            ["MultiBlend4_et1_snp_Texture2D_14_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["MultiBlend4_et1_snp_Texture2D_13_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend4_et1_snp_Texture2D_15_GSBlendMap_NormalMap_2"] = TexKind.Normal,
            ["MultiBlend4_et1_snp_Texture2D_16_GSBlendMap_NormalMap_3"] = TexKind.Normal,
            ["C_ARSN_cinder_snp_Texture2D_4_NormalMap_0"] = TexKind.Normal,
            ["RockBlend_snp_Texture2D_13_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend3_et1_e_snp_Texture2D_14_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["MultiBlend2_et1_e_snp_Texture2D_13_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend3_et1_e_snp_Texture2D_13_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["C_ARSN_cinder_em_snp_Texture2D_4_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend2_et1_em_snp_Texture2D_13_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["RockBlend_snp_Texture2D_14_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["SAT_MultiBlend2_BlendOpacity_Equip_Cm_snp_Texture2D_7_GSBlendMap_NormalMap_2"] = TexKind.Normal,
            ["g_BumpmapTexture3"] = TexKind.Normal,
            ["SAT_Equip_em_snp_Texture2D_4_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend2_et1_em_snp_Texture2D_14_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["MultiBlend3_et1_e_snp_Texture2D_15_GSBlendMap_NormalMap_2"] = TexKind.Normal,
            ["Veill_snp_Texture2D_1_NormalMap_0"] = TexKind.Normal,
            ["Fur_FDP_snp_Texture2D_3_NormalMap_0"] = TexKind.Normal,
            ["Fur_FDP_snp_Texture2D_4_NormalMap_1"] = TexKind.Normal,
            ["C_ARSN_em_snp_Texture2D_4_NormalMap_0"] = TexKind.Normal,
            ["SAT_Equip_SSS_snp_Texture2D_4_NormalMap_0"] = TexKind.Normal,
            ["SAT_Equip_Veill_snp_Texture2D_4_NormalMap_0"] = TexKind.Normal,
            ["RecursiveCrystal_snp_Texture2D_3_NormalMap_0"] = TexKind.Normal,
            ["SAT_MultiBlend2_BlendOpacity_Equip_Cm_snp_Texture2D_14_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["SAT_MultiBlend2_BlendOpacity_Equip_Cm_snp_Texture2D_13_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["FlowmapVertexOffset_snp_Texture2D_3_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend4_et1_e_snp_Texture2D_16_GSBlendMap_NormalMap_3"] = TexKind.Normal,
            ["Interior_snp_Texture2D_0_NormalMap_0"] = TexKind.Normal,
            ["Veill_NoLight_snp_Texture2D_1_NormalMap_0"] = TexKind.Normal,
            ["BloodBlend_snp_Texture2D_14_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["Crystal_snp_Texture2D_0_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend2_et1_e_snp_Texture2D_14_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["SAT_Equip_Df_snp_Texture2D_4_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend2_et1_em_Mask_snp_Texture2D_14_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["MultiBlend2_et1_em_Mask_snp_Texture2D_13_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["BloodBlend_snp_Texture2D_7_GSBlendMap_NormalMap_2"] = TexKind.Normal,
            ["BloodBlend_snp_Texture2D_8_GSBlendMap_NormalMap_3"] = TexKind.Normal,
            ["BurnCloth_snp_Texture2D_4_NormalMap_0"] = TexKind.Normal,
            ["SandBlend_2_snp_Texture2D_5_GSBlendMap_NormalMap_3"] = TexKind.Normal,
            ["SandBlend_2_snp_Texture2D_14_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["SandBlend_2_snp_Texture2D_3_GSBlendMap_NormalMap_5"] = TexKind.Normal,
            ["SandBlend_2_snp_Texture2D_6_GSBlendMap_NormalMap_4"] = TexKind.Normal,
            ["SandBlend_2_snp_Texture2D_0_GSBlendMap_NormalMap_2"] = TexKind.Normal,
            ["SandBlend_snp_Texture2D_5_GSBlendMap_NormalMap_3"] = TexKind.Normal,
            ["SandBlend_snp_Texture2D_14_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["SandBlend_snp_Texture2D_3_GSBlendMap_NormalMap_5"] = TexKind.Normal,
            ["SandBlend_snp_Texture2D_6_GSBlendMap_NormalMap_4"] = TexKind.Normal,
            ["SandBlend_snp_Texture2D_0_GSBlendMap_NormalMap_2"] = TexKind.Normal,
            ["Rotten_snp_Texture2D_1_NormalMap_1"] = TexKind.Normal,
            ["Rotten_snp_Texture2D_5_NormalMap_0"] = TexKind.Normal,
            ["BloodBlend_snp_Texture2D_13_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["SandBlend_snp_Texture2D_13_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend2_et1_mt1_snp_Texture2D_2_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend4_et1_mt1_snp_Texture2D_3_GSBlendMap_NormalMap_3"] = TexKind.Normal,
            ["MultiBlend2_et1_mt1_snp_Texture2D_5_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["MultiBlend4_et1_mt1_snp_Texture2D_4_GSBlendMap_NormalMap_2"] = TexKind.Normal,
            ["MultiBlend4_et1_mt1_snp_Texture2D_2_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend4_et1_mt1_snp_Texture2D_5_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["MultiBlend2_et1_p_snp_Texture2D_13_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend2_et1_p_snp_Texture2D_14_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["FlowmapVertexOffset_em_snp_Texture2D_3_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend2_et1_em_Mask2_snp_Texture2D_13_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend2_et1_em_Mask2_snp_Texture2D_14_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["MultiBlend3_et1_mp1_snp_Texture2D_2_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend3_et1_mp1_snp_Texture2D_5_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["MultiBlend3_et1_mp1_snp_Texture2D_4_GSBlendMap_NormalMap_2"] = TexKind.Normal,
            ["MultiBlend4_et1_e_snp_Texture2D_13_GSBlendMap_NormalMap_0"] = TexKind.Normal,
            ["MultiBlend4_et1_e_snp_Texture2D_14_GSBlendMap_NormalMap_1"] = TexKind.Normal,
            ["MultiBlend4_et1_e_snp_Texture2D_15_GSBlendMap_NormalMap_2"] = TexKind.Normal,
            ["SAT_Equip_Df_snp_Texture2D_7_NormalMap_1"] = TexKind.Normal,

            // Reflective
            ["g_SpecularTexture"] = TexKind.Reflective,
            ["SAT_Equip_snp_Texture2D_3_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend2_et1_snp_Texture2D_5_GSBlendMap_ReflectanceMap_0"] = TexKind.Reflective,
            ["g_SpecularTexture2"] = TexKind.Reflective,
            ["MultiBlend2_et1_snp_Texture2D_6_GSBlendMap_ReflectanceMap_1"] = TexKind.Reflective,
            ["MultiBlend3_et1_snp_Texture2D_6_GSBlendMap_ReflectanceMap_1"] = TexKind.Reflective,
            ["MultiBlend3_et1_snp_Texture2D_7_GSBlendMap_ReflectanceMap_2"] = TexKind.Reflective,
            ["MultiBlend3_et1_snp_Texture2D_5_GSBlendMap_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend4_et1_snp_Texture2D_5_GSBlendMap_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend4_et1_snp_Texture2D_6_GSBlendMap_ReflectanceMap_1"] = TexKind.Reflective,
            ["C_ARSN_cinder_snp_Texture2D_3_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend4_et1_snp_Texture2D_7_GSBlendMap_ReflectanceMap_2"] = TexKind.Reflective,
            ["MultiBlend4_et1_snp_Texture2D_8_GSBlendMap_ReflectanceMap_3"] = TexKind.Reflective,
            ["C_ARSN_cinder_em_snp_Texture2D_3_ReflectanceMap_0"] = TexKind.Reflective,
            ["SAT_Equip_em_snp_Texture2D_3_ReflectanceMap_0"] = TexKind.Reflective,
            ["Fur_FDP_snp_Texture2D_0_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend2_et1_e_snp_Texture2D_5_GSBlendMap_ReflectanceMap_0"] = TexKind.Reflective,
            ["C_ARSN_em_snp_Texture2D_3_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend2_et1_em_snp_Texture2D_5_GSBlendMap_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend3_et1_e_snp_Texture2D_7_GSBlendMap_ReflectanceMap_2"] = TexKind.Reflective,
            ["MultiBlend3_et1_e_snp_Texture2D_6_GSBlendMap_ReflectanceMap_1"] = TexKind.Reflective,
            ["MultiBlend3_et1_e_snp_Texture2D_5_GSBlendMap_ReflectanceMap_0"] = TexKind.Reflective,
            ["SAT_Equip_Veill_snp_Texture2D_3_ReflectanceMap_0"] = TexKind.Reflective,
            ["SAT_Equip_SSS_snp_Texture2D_3_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend2_et1_em_snp_Texture2D_6_GSBlendMap_ReflectanceMap_1"] = TexKind.Reflective,
            ["SAT_MultiBlend2_BlendOpacity_Equip_Cm_snp_Texture2D_5_GSBlendMap_ReflectanceMap_0"] = TexKind.Reflective,
            ["SAT_MultiBlend2_BlendOpacity_Equip_Cm_snp_Texture2D_6_GSBlendMap_ReflectanceMap_1"] = TexKind.Reflective,
            ["RecursiveCrystal_snp_Texture2D_1_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend2_et1_e_snp_Texture2D_6_GSBlendMap_ReflectanceMap_1"] = TexKind.Reflective,
            ["MultiBlend2_et1_p_snp_Texture2D_5_GSBlendMap_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend2_et1_p_snp_Texture2D_6_GSBlendMap_ReflectanceMap_1"] = TexKind.Reflective,
            ["BurnCloth_snp_Texture2D_0_ReflectanceMap_0"] = TexKind.Reflective,
            ["SAT_Equip_Df_snp_Texture2D_8_ReflectanceMap_1"] = TexKind.Reflective,
            ["SAT_Equip_Df_snp_Texture2D_3_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend2_et1_em_Mask_snp_Texture2D_6_GSBlendMap_ReflectanceMap_1"] = TexKind.Reflective,
            ["MultiBlend2_et1_em_Mask_snp_Texture2D_5_GSBlendMap_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend4_et1_mt1_snp_Texture2D_13_GSBlendMap_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend4_et1_mt1_snp_Texture2D_12_GSBlendMap_ReflectanceMap_1"] = TexKind.Reflective,
            ["MultiBlend4_et1_mt1_snp_Texture2D_11_GSBlendMap_ReflectanceMap_2"] = TexKind.Reflective,
            ["MultiBlend4_et1_mt1_snp_Texture2D_10_GSBlendMap_ReflectanceMap_3"] = TexKind.Reflective,
            ["Crystal_snp_Texture2D_2_GSBlendMap_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend2_et1_mt1_snp_Texture2D_13_GSBlendMap_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend2_et1_mt1_snp_Texture2D_12_GSBlendMap_ReflectanceMap_1"] = TexKind.Reflective,
            ["MultiBlend2_et1_em_Mask2_snp_Texture2D_5_GSBlendMap_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend2_et1_em_Mask2_snp_Texture2D_6_GSBlendMap_ReflectanceMap_1"] = TexKind.Reflective,
            ["Interior_snp_Texture2D_2_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend4_et1_e_snp_Texture2D_8_GSBlendMap_ReflectanceMap_3"] = TexKind.Reflective,
            ["MultiBlend3_et1_mp1_snp_Texture2D_13_GSBlendMap_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend3_et1_mp1_snp_Texture2D_12_GSBlendMap_ReflectanceMap_1"] = TexKind.Reflective,
            ["MultiBlend3_et1_mp1_snp_Texture2D_11_GSBlendMap_ReflectanceMap_2"] = TexKind.Reflective,
            ["MultiBlend4_et1_e_snp_Texture2D_5_GSBlendMap_ReflectanceMap_0"] = TexKind.Reflective,
            ["MultiBlend4_et1_e_snp_Texture2D_6_GSBlendMap_ReflectanceMap_1"] = TexKind.Reflective,
            ["MultiBlend4_et1_e_snp_Texture2D_7_GSBlendMap_ReflectanceMap_2"] = TexKind.Reflective,
            ["FlowmapVertexOffset_snp_Texture2D_1_ReflectanceMap_0"] = TexKind.Reflective,
            ["FlowmapVertexOffset_em_snp_Texture2D_1_ReflectanceMap_0"] = TexKind.Reflective,

            // Shininess
            ["g_ShininessTexture"] = TexKind.Shininess,
            ["MultiBlend2_et1_snp_Texture2D_9_GSBlendMap_ShininessMap_0"] = TexKind.Shininess,
            ["g_ShininessTexture2"] = TexKind.Shininess,
            ["MultiBlend2_et1_snp_Texture2D_10_GSBlendMap_ShininessMap_1"] = TexKind.Shininess,
            ["MultiBlend3_et1_snp_Texture2D_10_GSBlendMap_ShininessMap_1"] = TexKind.Shininess,
            ["MultiBlend3_et1_snp_Texture2D_11_GSBlendMap_ShininessMap_2"] = TexKind.Shininess,
            ["MultiBlend3_et1_snp_Texture2D_9_GSBlendMap_ShininessMap_0"] = TexKind.Shininess,
            ["MultiBlend4_et1_snp_Texture2D_10_GSBlendMap_ShininessMap_1"] = TexKind.Shininess,
            ["MultiBlend4_et1_snp_Texture2D_9_GSBlendMap_ShininessMap_0"] = TexKind.Shininess,
            ["MultiBlend4_et1_snp_Texture2D_11_GSBlendMap_ShininessMap_2"] = TexKind.Shininess,
            ["MultiBlend4_et1_snp_Texture2D_12_GSBlendMap_ShininessMap_3"] = TexKind.Shininess,
            ["MultiBlend3_et1_e_snp_Texture2D_9_GSBlendMap_ShininessMap_0"] = TexKind.Shininess,
            ["MultiBlend3_et1_e_snp_Texture2D_10_GSBlendMap_ShininessMap_1"] = TexKind.Shininess,
            ["MultiBlend3_et1_e_snp_Texture2D_11_GSBlendMap_ShininessMap_2"] = TexKind.Shininess,
            ["RecursiveCrystal_snp_Texture2D_4_ShininessMap_0"] = TexKind.Shininess,
            ["SAT_MultiBlend2_BlendOpacity_Equip_Cm_snp_Texture2D_9_GSBlendMap_ShininessMap_0"] = TexKind.Shininess,
            ["SAT_MultiBlend2_BlendOpacity_Equip_Cm_snp_Texture2D_10_GSBlendMap_ShininessMap_1"] = TexKind.Shininess,
            ["MultiBlend4_et1_e_snp_Texture2D_9_GSBlendMap_ShininessMap_0"] = TexKind.Shininess,
            ["MultiBlend4_et1_e_snp_Texture2D_10_GSBlendMap_ShininessMap_1"] = TexKind.Shininess,
            ["MultiBlend4_et1_e_snp_Texture2D_11_GSBlendMap_ShininessMap_2"] = TexKind.Shininess,
            ["MultiBlend4_et1_e_snp_Texture2D_12_GSBlendMap_ShininessMap_3"] = TexKind.Shininess,
            ["Interior_snp_Texture2D_3_ShininessMap_0"] = TexKind.Shininess,
            ["MultiBlend2_et1_p_snp_Texture2D_9_GSBlendMap_ShininessMap_0"] = TexKind.Shininess,
            ["MultiBlend2_et1_p_snp_Texture2D_10_GSBlendMap_ShininessMap_1"] = TexKind.Shininess,
            ["MultiBlend2_et1_e_snp_Texture2D_9_GSBlendMap_ShininessMap_0"] = TexKind.Shininess,
            ["MultiBlend2_et1_e_snp_Texture2D_10_GSBlendMap_ShininessMap_1"] = TexKind.Shininess,
            ["MultiBlend2_et1_em_Mask_snp_Texture2D_10_GSBlendMap_ShininessMap_1"] = TexKind.Shininess,
            ["MultiBlend2_et1_em_Mask_snp_Texture2D_9_GSBlendMap_ShininessMap_0"] = TexKind.Shininess,
            ["BloodBlend_snp_Texture2D_10_GSBlendMap_ShininessMap_1"] = TexKind.Shininess,
            ["BloodBlend_snp_Texture2D_9_GSBlendMap_ShininessMap_0"] = TexKind.Shininess,
            ["BurnCloth_snp_Texture2D_1_ShininessMap_0"] = TexKind.Shininess,
            ["MultiBlend4_et1_mt1_snp_Texture2D_9_GSBlendMap_ShininessMap_0"] = TexKind.Shininess,
            ["MultiBlend4_et1_mt1_snp_Texture2D_8_GSBlendMap_ShininessMap_1"] = TexKind.Shininess,
            ["MultiBlend4_et1_mt1_snp_Texture2D_7_GSBlendMap_ShininessMap_2"] = TexKind.Shininess,
            ["MultiBlend4_et1_mt1_snp_Texture2D_6_GSBlendMap_ShininessMap_3"] = TexKind.Shininess,
            ["MultiBlend2_et1_mt1_snp_Texture2D_9_GSBlendMap_ShininessMap_0"] = TexKind.Shininess,
            ["MultiBlend2_et1_mt1_snp_Texture2D_8_GSBlendMap_ShininessMap_1"] = TexKind.Shininess,
            ["MultiBlend2_et1_em_snp_Texture2D_9_GSBlendMap_ShininessMap_0"] = TexKind.Shininess,
            ["MultiBlend2_et1_em_snp_Texture2D_10_GSBlendMap_ShininessMap_1"] = TexKind.Shininess,
            ["MultiBlend2_et1_em_Mask2_snp_Texture2D_9_GSBlendMap_ShininessMap_0"] = TexKind.Shininess,
            ["MultiBlend2_et1_em_Mask2_snp_Texture2D_10_GSBlendMap_ShininessMap_1"] = TexKind.Shininess,
            ["MultiBlend3_et1_mp1_snp_Texture2D_9_GSBlendMap_ShininessMap_0"] = TexKind.Shininess,
            ["MultiBlend3_et1_mp1_snp_Texture2D_8_GSBlendMap_ShininessMap_1"] = TexKind.Shininess,
            ["MultiBlend3_et1_mp1_snp_Texture2D_7_GSBlendMap_ShininessMap_2"] = TexKind.Shininess,
            ["Crystal_snp_Texture2D_1_GSBlendMap_ShininessMap_0"] = TexKind.Shininess,

            // Emissive
            ["g_EmissiveTexture"] = TexKind.Emissive,
            ["C_ARSN_cinder_em_snp_Texture2D_6_EmissiveMap_1"] = TexKind.Emissive,
            ["SAT_Equip_em_snp_Texture2D_6_EmissiveMap_1"] = TexKind.Emissive,
            ["MultiBlend2_et1_em_snp_Texture2D_4_GSBlendMap_EmissiveMap_1"] = TexKind.Emissive,
            ["MultiBlend2_et1_em_snp_Texture2D_3_GSBlendMap_EmissiveMap_0"] = TexKind.Emissive,
            ["C_ARSN_em_snp_Texture2D_6_EmissiveMap_0"] = TexKind.Emissive,
            ["Interior_snp_Texture2D_1_EmissiveMap_0"] = TexKind.Emissive,
            ["MultiBlend2_et1_em_Mask_snp_Texture2D_3_GSBlendMap_EmissiveMap_0"] = TexKind.Emissive,
            ["MultiBlend2_et1_em_Mask_snp_Texture2D_4_GSBlendMap_EmissiveMap_1"] = TexKind.Emissive,
            ["g_EmissiveTexture2"] = TexKind.Emissive,
            ["C_ARSN_cinder_em_snp_Texture2D_0_EmissiveMap_0"] = TexKind.Emissive,
            ["FlowmapVertexOffset_em_snp_Texture2D_2_EmissiveMap_0"] = TexKind.Emissive,
            ["MultiBlend2_et1_em_Mask2_snp_Texture2D_3_GSBlendMap_EmissiveMap_0"] = TexKind.Emissive,
            ["MultiBlend2_et1_em_Mask2_snp_Texture2D_4_GSBlendMap_EmissiveMap_1"] = TexKind.Emissive,
            ["RecursiveCrystal_snp_Texture2D_5_ShininessMap_0"] = TexKind.Emissive,

            // Mask
            ["MultiBlend2_et1_snp_Texture2D_0_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["g_BlendMaskTexture"] = TexKind.Mask,
            ["MultiBlend3_et1_snp_Texture2D_0_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["MultiBlend4_et1_snp_Texture2D_0_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["Fujir_Nolight_OpacityBlend_snp_Texture2D_5_GSBlendMap_OpacityTexture2"] = TexKind.Mask,
            ["Fujir_Nolight_OpacityBlend_snp_Texture2D_4_GSBlendMap_OpacityTexture1"] = TexKind.Mask,
            ["MultiBlend2_et1_nolight_MuitiBlendMask_snp_Texture2D_3_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["MultiBlend3_et1_e_snp_Texture2D_0_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["MultiBlend2_et1_em_snp_Texture2D_0_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["SAT_MultiBlend2_BlendOpacity_Equip_Cm_snp_Texture2D_3_GSBlendMap_OpacityTexture"] = TexKind.Mask,
            ["MultiBlend2_et1_nolight_MuitiBlendMask_snp_Texture2D_0_GSBlendMap_ShininessMap_0"] = TexKind.Mask,
            ["Veill_snp_Texture2D_3_MaskTexture"] = TexKind.Mask,
            ["BloodBlend_snp_Texture2D_3_GSBlendMap_Blood_BlendMask"] = TexKind.Mask,
            ["BloodBlend_snp_Texture2D_0_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["BloodBlend_snp_Texture2D_11_GSBlendMap_Blood_BlendMask2"] = TexKind.Mask,
            ["MultiBlend2_et1_e_snp_Texture2D_0_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["Rotten_snp_Texture2D_12_BlendEdgeTexture"] = TexKind.Mask,
            ["BurnCloth_snp_Texture2D_2_BlendMap"] = TexKind.Mask,
            ["SandBlend_2_snp_Texture2D_2_GSBlendMap_BlendEdgeTexture2"] = TexKind.Mask,
            ["SandBlend_2_snp_Texture2D_4_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["SandBlend_snp_Texture2D_2_GSBlendMap_BlendEdgeTexture2"] = TexKind.Mask,
            ["SandBlend_snp_Texture2D_4_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["MultiBlend2_et1_nolight_snp_Texture2D_0_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["MultiBlend2_et1_em_Mask_snp_Texture2D_0_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["MultiBlend2_et1_mt1_snp_Texture2D_0_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["MultiBlend2_et1_mt1_snp_Texture2D_1_GSBlendMap_BlendMaskTexture"] = TexKind.Mask,
            ["SAT_MultiBlend2_BlendOpacity_Equip_Cm_snp_Texture2D_0_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["Veill_NoLight_snp_Texture2D_3_MaskTexture"] = TexKind.Mask,
            ["MultiBlend2_et1_em_Mask2_snp_Texture2D_0_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["MultiBlend2_et1_em_Mask_snp_Texture2D_7_GSBlendMap_EmissiveMask"] = TexKind.Mask,
            ["MultiBlend2_et1_em_Mask2_snp_Texture2D_11_GSBlendMap_EmissiveMask_0"] = TexKind.Mask,
            ["MultiBlend2_et1_em_Mask2_snp_Texture2D_8_GSBlendMap_EmissiveMask_1"] = TexKind.Mask,
            ["MultiBlend4_et1_e_snp_Texture2D_0_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["SAT_Equip_SSS_snp_Texture2D_6_SSSMask"] = TexKind.Mask,
            ["MultiBlend4_et1_mt1_snp_Texture2D_1_GSBlendMap_BlendMaskTexture"] = TexKind.Mask,
            ["MultiBlend3_et1_mp1_snp_Texture2D_0_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["MultiBlend2_et1_p_snp_Texture2D_0_GSBlendMap_BlendEdgeTexture"] = TexKind.Mask,
            ["g_ScatteringMaskTexture"] = TexKind.Mask,

            // Displacement/Height
            ["g_DisplacementTexture"] = TexKind.Height,
            ["FlowmapVertexOffset_snp_Texture2D_5_DisplacementMap_0"] = TexKind.Height,
            ["g_SnowHeightTexture"] = TexKind.Height,

            // Flow/VertexOffset
            ["FlowmapVertexOffset_snp_Texture2D_4_FlowMap_0"] = TexKind.VertexOffset,
            ["g_FlowTexture"] = TexKind.VertexOffset,
            ["FlowmapVertexOffset_em_snp_Texture2D_4_FlowMap_0"] = TexKind.VertexOffset,
            ["BloodBlend_snp_Texture2D_4_GSBlendMap_FlowMap_0"] = TexKind.VertexOffset,
            ["Rotten_snp_Texture2D_4_FlowMap_0"] = TexKind.VertexOffset
        };
        public static TexKind GetTexKind(this FLVER2.Texture texture)
        {
            if (_textureTypeMap.TryGetValue(texture.Type, out var kind))
                return kind;
            return TexKind.Unknown;
        }
    }
}
