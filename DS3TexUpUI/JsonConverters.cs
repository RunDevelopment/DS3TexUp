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
    public static class JsonConverters
    {
        public static readonly JsonConverter[] Converters = new JsonConverter[] {
            new Vector2Converter(),
            new TexIdConverter(),
            new TexIdDictionaryConverter<int>(),
            new TexIdDictionaryConverter<TexKind>(),
            new TexIdDictionaryConverter<TransparencyKind>(),
        };

        public static JsonSerializerOptions WithStandardConverters(this JsonSerializerOptions options)
        {
            foreach (var c in Converters)
                options.Converters.Add(c);
            return options;
        }

        public static void SaveAsJson<T>(this T value, string file)
        {
            if (File.Exists(file)) File.Delete(file);
            using var f = File.OpenWrite(file);
            var jw = new Utf8JsonWriter(f);
            JsonSerializer.Serialize(jw, value, new JsonSerializerOptions().WithStandardConverters());
        }

        public static T LoadJsonFile<T>(this string file)
        {
            var jr = new Utf8JsonReader(File.ReadAllBytes(file));
            var options = new JsonSerializerOptions().WithStandardConverters();
            return JsonSerializer.Deserialize<T>(ref jr, options);
        }

        private sealed class Vector2Converter : JsonConverter<Vector2>
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

        private sealed class TexIdConverter : JsonConverter<TexId>
        {
            public override TexId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.String) throw new JsonException();
                var value = reader.GetString();
                reader.Read();
                return new TexId(value);
            }

            public override void Write(Utf8JsonWriter writer, TexId value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.Value);
            }
        }

        private sealed class TexIdDictionaryConverter<V> : JsonConverter<Dictionary<TexId, V>>
        {
            public override Dictionary<TexId, V> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();

                var result = new Dictionary<TexId, V>();

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject) return result;

                    if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();

                    var id = new TexId(reader.GetString());
                    reader.Read();

                    var value = JsonSerializer.Deserialize<V>(ref reader, options);

                    result[id] = value;
                }

                throw new JsonException();
            }

            public override void Write(Utf8JsonWriter writer, Dictionary<TexId, V> value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();

                var sorted = value.ToList();
                sorted.Sort((a, b) => a.Key.CompareTo(b.Key));

                foreach (var pair in sorted)
                {
                    writer.WritePropertyName(pair.Key.Value);
                    JsonSerializer.Serialize(writer, pair.Value, options);
                }

                writer.WriteEndObject();
            }
        }
    }
}
