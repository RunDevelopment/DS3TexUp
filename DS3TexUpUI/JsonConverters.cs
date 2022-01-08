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
using SoulsFormats;

namespace DS3TexUpUI
{
    public static class JsonConverters
    {
        private static readonly Dictionary<Type, JsonConverter> _concreteTypes = new Dictionary<Type, JsonConverter>()
        {
            [typeof(Vector2)] = new Vector2Converter(),
            [typeof(TexId)] = new TexIdConverter(),
            [typeof(Size)] = new SizeConverter(),
            [typeof(DDSFormat)] = new DDSFormatConverter(),
        };
        public static JsonSerializerOptions WithConvertersFor<T>(this JsonSerializerOptions options)
        {
            var types = GetTypes(typeof(T));

            foreach (var type in types)
            {
                if (_concreteTypes.TryGetValue(type, out var concreteConverter))
                {
                    options.Converters.Add(concreteConverter);
                }
                else if (type.IsGenericType)
                {
                    var args = type.GetGenericArguments();
                    var typeDef = type.GetGenericTypeDefinition();

                    if (typeDef == typeof(Dictionary<,>))
                    {
                        if (args[0] == typeof(TexId))
                        {
                            var cType = typeof(TexIdDictionaryConverter<>).MakeGenericType(args[1]);
                            var ctor = cType.GetConstructor(new Type[0]);
                            var c = ctor.Invoke(null);
                            options.Converters.Add((JsonConverter)c);
                        }
                    }
                    else if (typeDef == typeof(ValueTuple<,>))
                    {
                        var cType = typeof(Tuple2Converter<,>).MakeGenericType(args);
                        var ctor = cType.GetConstructor(new Type[0]);
                        var c = ctor.Invoke(null);
                        options.Converters.Add((JsonConverter)c);
                    }
                }
            }

            return options;
        }
        private static List<Type> GetTypes(Type type)
        {
            var types = new List<Type>();
            BFS(type, (type, list) =>
            {
                types.Add(type);

                // Type parameters
                if (type.IsGenericType)
                    list.AddRange(type.GetGenericArguments());

                // Properties
                list.AddRange(GetPropertyTypes(type));

                // IEnumerable<T> implementation
                var enumerableTypes = type.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    .SelectMany(i => i.GetGenericArguments());
                list.AddRange(enumerableTypes);
            });
            return types;
        }
        private static IEnumerable<Type> GetPropertyTypes(Type type)
        {
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var p in props)
            {
                var get = p.GetMethod;
                if (get != null && get.IsPublic)
                {
                    yield return get.ReturnType;
                }
            }
        }
        private static void BFS<T>(T start, Action<T, List<T>> consumeNext)
        {
            var seen = new HashSet<T>();

            var current = new List<T>() { start };
            var next = new List<T>();
            while (current.Count > 0)
            {
                next.Clear();

                foreach (var item in current)
                {
                    if (seen.Contains(item)) continue;
                    seen.Add(item);

                    consumeNext(item, next);
                }

                (current, next) = (next, current);
            }
        }

        public static void SaveAsJson<T>(this T value, string file)
        {
            if (File.Exists(file)) File.Delete(file);
            using var f = File.OpenWrite(file);
            var jw = new Utf8JsonWriter(f);
            var options = new JsonSerializerOptions().WithConvertersFor<T>();
            JsonSerializer.Serialize(jw, value, options);
        }

        public static T LoadJsonFile<T>(this string file)
        {
            var jr = new Utf8JsonReader(File.ReadAllBytes(file));
            var options = new JsonSerializerOptions().WithConvertersFor<T>();
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

        private sealed class Tuple2Converter<A, B> : JsonConverter<(A, B)>
        {
            public override (A, B) Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException();

                reader.Read();
                var item1 = JsonSerializer.Deserialize<A>(ref reader, options);
                reader.Read();
                var item2 = JsonSerializer.Deserialize<B>(ref reader, options);
                reader.Read();
                if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException();

                return (item1, item2);
            }

            public override void Write(Utf8JsonWriter writer, (A, B) value, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                JsonSerializer.Serialize(writer, value.Item1, options);
                JsonSerializer.Serialize(writer, value.Item2, options);
                writer.WriteEndArray();
            }
        }

        private sealed class TexIdConverter : JsonConverter<TexId>
        {
            public override TexId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.String) throw new JsonException();
                return new TexId(reader.GetString());
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

        private sealed class SizeConverter : JsonConverter<Size>
        {
            public override Size Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var s = JsonSerializer.Deserialize<Surrogate>(ref reader, options);
                return new Size(s.Width, s.Height);
            }

            public override void Write(Utf8JsonWriter writer, Size value, JsonSerializerOptions options)
            {
                var s = new Surrogate() { Width = value.Width, Height = value.Height };
                JsonSerializer.Serialize(writer, s, options);
            }

            private struct Surrogate
            {
                public int Width { get; set; }
                public int Height { get; set; }
            }
        }

        private sealed class DDSFormatConverter : JsonConverter<DDSFormat>
        {
            public override DDSFormat Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.String) throw new JsonException();
                return DDSFormat.Parse(reader.GetString());
            }
            public override void Write(Utf8JsonWriter writer, DDSFormat value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.ToString());
            }
        }
    }
}
