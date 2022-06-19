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

#nullable enable

namespace DS3TexUpUI
{
    public static class JsonConverters
    {
        interface IAdditionalConverters
        {
            IEnumerable<JsonConverter> GetConverters();
        }
        private static readonly Dictionary<Type, JsonConverter> _concreteTypes = new Dictionary<Type, JsonConverter>()
        {
            [typeof(Vector2)] = new Vector2Converter(),
            [typeof(TexId)] = new TexIdConverter(),
            [typeof(Size)] = new SizeConverter(),
            [typeof(RgbaDiff)] = new RgbaDiffConverter(),
            [typeof(DDSFormat)] = new StringParsingConverter<DDSFormat>(DDSFormat.Parse),
            [typeof(ColorCode6x6)] = new StringParsingConverter<ColorCode6x6>(ColorCode6x6.Parse),
            [typeof(Tile)] = new StringParsingConverter<Tile>(Tile.Parse),
            [typeof(HashSet<TexId>)] = new TexIdHashSetConverter(),
            [typeof(DS3.AlbedoNormalReflective)] = new AlbedoNormalReflectiveConverter(),
            [typeof(List<Metalness.DataPoint>)] = new MetalnessDataPointsConverter(),
        };
        public static JsonSerializerOptions WithConvertersFor<T>(this JsonSerializerOptions options)
        {
            var types = GetTypes(typeof(T));

            var converters = new HashSet<JsonConverter>();
            void AddConverter(JsonConverter c)
            {
                converters.Add(c);
                if (c is IAdditionalConverters ac)
                {
                    foreach (var other in ac.GetConverters())
                        converters.Add(other);
                }
            }

            foreach (var type in types)
            {
                if (_concreteTypes.TryGetValue(type, out var concreteConverter))
                {
                    AddConverter(concreteConverter);
                }
                else if (type.IsGenericType)
                {
                    var args = type.GetGenericArguments();
                    var typeDef = type.GetGenericTypeDefinition();

                    if (typeDef == typeof(Dictionary<,>))
                    {
                        object? stringConverter = null;
                        if (args[0] == typeof(TexId))
                            stringConverter = TexIdStringConverter.Instance;
                        else if (args[0] == typeof(string))
                            stringConverter = StringStringConverter.Instance;

                        if (stringConverter != null)
                        {
                            var cType = typeof(StringDictionaryConverter<,>).MakeGenericType(args[0], args[1]);
                            var ctor = cType.GetConstructor(new Type[] { typeof(StringConverter<>).MakeGenericType(args[0]) })!;
                            var c = ctor.Invoke(new object[] { stringConverter });
                            AddConverter((JsonConverter)c);
                        }
                    }
                    else if (typeDef == typeof(ValueTuple<,>))
                    {
                        var cType = typeof(Tuple2Converter<,>).MakeGenericType(args);
                        var ctor = cType.GetConstructor(new Type[0])!;
                        var c = ctor.Invoke(null);
                        AddConverter((JsonConverter)c);
                    }
                    else if (typeDef == typeof(EquivalenceCollection<>))
                    {
                        var cType = typeof(EquivalenceCollectionConverter<>).MakeGenericType(args);
                        var ctor = cType.GetConstructor(new Type[0])!;
                        var c = ctor.Invoke(null);
                        AddConverter((JsonConverter)c);
                    }
                    else if (typeDef == typeof(DifferenceCollection<>))
                    {
                        var cType = typeof(DifferenceCollectionConverter<>).MakeGenericType(args);
                        var ctor = cType.GetConstructor(new Type[0])!;
                        var c = ctor.Invoke(null);
                        AddConverter((JsonConverter)c);
                    }
                }
            }

            foreach (var c in converters)
                options.Converters.Add(c);

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

        public static void Serialize<T>(Utf8JsonWriter writer, T value, JsonSerializerOptions? options = null)
        {
            options = (options ?? new JsonSerializerOptions()).WithConvertersFor<T>();
            JsonSerializer.Serialize(writer, value, options);
        }
        public static T Deserialize<T>(ref Utf8JsonReader reader, JsonSerializerOptions? options = null)
        {
            options = (options ?? new JsonSerializerOptions()).WithConvertersFor<T>();
            return JsonSerializer.Deserialize<T>(ref reader, options);
        }

        public static void SaveAsJson<T>(this T value, string file, bool formatted = true)
        {
            if (File.Exists(file)) File.Delete(file);

            if (formatted)
            {
                // We want tab indentation and LF new lines
                using var ms = new MemoryStream();
                Serialize(new Utf8JsonWriter(ms, new JsonWriterOptions() { Indented = formatted }), value);

                var bytes = ms.ToArray();
                using var f = File.OpenWrite(file);

                for (int i = 0; i < bytes.Length; i++)
                {
                    var b = bytes[i];
                    if (b == '\r')
                    {
                        // convert line ends and indentation
                        f.WriteByte((byte)'\n');
                        i++; // skip \n of \r\n

                        uint spaces = 0;
                        while (i + 1 < bytes.Length && bytes[i + 1] == ' ')
                        {
                            spaces++;
                            i++;
                        }

                        spaces >>= 1;
                        for (; spaces != 0; spaces--)
                            f.WriteByte((byte)'\t');
                    }
                    else
                    {
                        f.WriteByte(b);
                    }
                }

                // add final new line
                f.WriteByte((byte)'\n');
            }
            else
            {
                using var f = File.OpenWrite(file);
                Serialize(new Utf8JsonWriter(f), value);
            }
        }

        public static T LoadJsonFile<T>(this string file)
        {
            var jr = new Utf8JsonReader(File.ReadAllBytes(file), new JsonReaderOptions());
            return Deserialize<T>(ref jr);
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

        private sealed class StringDictionaryConverter<S, V> : JsonConverter<Dictionary<S, V>>
            where S : notnull
        {
            public readonly StringConverter<S> Converter;
            public StringDictionaryConverter(StringConverter<S> converter)
            {
                Converter = converter;
            }
            public override Dictionary<S, V> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();

                var result = new Dictionary<S, V>();

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject) return result;

                    if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();

                    var id = Converter.Parse(reader.GetString());
                    reader.Read();

                    var value = JsonSerializer.Deserialize<V>(ref reader, options);

                    result[id] = value;
                }

                throw new JsonException();
            }

            public override void Write(Utf8JsonWriter writer, Dictionary<S, V> value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();

                var sorted = value.ToList();
                var comp = Comparer<S>.Default;
                sorted.Sort((a, b) => comp.Compare(a.Key, b.Key));

                foreach (var pair in sorted)
                {
                    writer.WritePropertyName(Converter.Stringify(pair.Key));
                    JsonSerializer.Serialize(writer, pair.Value, options);
                }

                writer.WriteEndObject();
            }
        }

        interface StringConverter<S>
        {
            string Stringify(S value);
            S Parse(string value);
        }
        public sealed class StringStringConverter : StringConverter<string>
        {
            public static readonly StringStringConverter Instance = new StringStringConverter();
            private StringStringConverter() { }
            public string Parse(string value) => value;
            public string Stringify(string value) => value;
        }
        public sealed class TexIdStringConverter : StringConverter<TexId>
        {
            public static readonly TexIdStringConverter Instance = new TexIdStringConverter();
            private TexIdStringConverter() { }
            public TexId Parse(string value) => new TexId(value);
            public string Stringify(TexId value) => value.Value;
        }

        private sealed class EquivalenceCollectionConverter<T> : JsonConverter<EquivalenceCollection<T>>
            where T : notnull
        {
            public override EquivalenceCollection<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var surrogate = JsonSerializer.Deserialize<List<HashSet<T>>>(ref reader, options);
                return new EquivalenceCollection<T>(surrogate);
            }

            public override void Write(Utf8JsonWriter writer, EquivalenceCollection<T> value, JsonSerializerOptions options)
            {
                var l = value.Classes.Select(eqClass =>
                {
                    var l = eqClass.ToList();
                    l.Sort();
                    return l;
                }).ToList();

                var com = Comparer<T>.Default;
                l.Sort((a, b) => com.Compare(a[0], b[0]));

                JsonSerializer.Serialize(writer, l, options);
            }
        }
        private sealed class DifferenceCollectionConverter<T> : JsonConverter<DifferenceCollection<T>>, IAdditionalConverters
            where T : notnull
        {
            private static readonly JsonConverter[] _converters = new JsonConverter[]{
                new Tuple2Converter<T, T>(),
            };
            public IEnumerable<JsonConverter> GetConverters() => _converters;

            public override DifferenceCollection<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var pairs = JsonSerializer.Deserialize<List<(T, T)>>(ref reader, options);
                var d = new DifferenceCollection<T>();
                foreach (var (a, b) in pairs)
                    d.Set(a, b);
                return d;
            }

            public override void Write(Utf8JsonWriter writer, DifferenceCollection<T> value, JsonSerializerOptions options)
            {
                var pairs = new List<(T, T)>();
                foreach (var (a, other) in value)
                    foreach (var b in other)
                        pairs.Add((a, b));
                pairs.Sort();

                var seen = new HashSet<(T, T)>();
                var unique = pairs.Where(p =>
                {
                    var (a, b) = p;
                    if (seen.Contains((a, b))) return false;
                    seen.Add((a, b));
                    seen.Add((b, a));
                    return true;
                }).ToList();

                JsonSerializer.Serialize(writer, unique, options);
            }
        }

        private sealed class TexIdHashSetConverter : JsonConverter<HashSet<TexId>>
        {
            public override HashSet<TexId> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var list = JsonSerializer.Deserialize<List<TexId>>(ref reader, options);
                return new HashSet<TexId>(list);
            }
            public override void Write(Utf8JsonWriter writer, HashSet<TexId> value, JsonSerializerOptions options)
            {
                var list = value.ToList();
                list.Sort();
                JsonSerializer.Serialize(writer, list, options.WithConvertersFor<List<TexId>>());
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

        private sealed class RgbaDiffConverter : JsonConverter<RgbaDiff>
        {
            public override RgbaDiff Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var s = JsonSerializer.Deserialize<Surrogate>(ref reader, options);
                return new RgbaDiff(s.R, s.G, s.B, s.A);
            }

            public override void Write(Utf8JsonWriter writer, RgbaDiff value, JsonSerializerOptions options)
            {
                JsonSerializer.Serialize(writer, new Surrogate { R = value.R, G = value.G, B = value.B, A = value.A }, options);
            }

            private struct Surrogate
            {
                public byte R { get; set; }
                public byte G { get; set; }
                public byte B { get; set; }
                public byte A { get; set; }
            }
        }

        private sealed class AlbedoNormalReflectiveConverter : JsonConverter<DS3.AlbedoNormalReflective>
        {
            public override DS3.AlbedoNormalReflective Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var s = JsonSerializer.Deserialize<Surrogate>(ref reader, options);
                return new DS3.AlbedoNormalReflective(s.A, s.N, s.R);
            }

            public override void Write(Utf8JsonWriter writer, DS3.AlbedoNormalReflective value, JsonSerializerOptions options)
            {
                var s = new Surrogate() { A = value.A, N = value.N, R = value.R };
                JsonSerializer.Serialize(writer, s, options);
            }

            private struct Surrogate
            {
                public TexId A { get; set; }
                public TexId N { get; set; }
                public TexId R { get; set; }
            }
        }

        private sealed class MetalnessDataPointsConverter : JsonConverter<List<Metalness.DataPoint>>
        {
            public override List<Metalness.DataPoint> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, bool>>(ref reader, options);
                return d.Select(kv => new Metalness.DataPoint(StringToMaterial(kv.Key), kv.Value)).ToList();
            }

            public override void Write(Utf8JsonWriter writer, List<Metalness.DataPoint> value, JsonSerializerOptions options)
            {
                var d = value.ToDictionary(v => MaterialToString(v.Material), v => v.Metal);
                JsonSerializer.Serialize(writer, d, options);
            }


            private static string MaterialToString(Metalness.MaterialPoint m)
            {
                return $"{m.A.R:x2}{m.A.G:x2}{m.A.B:x2}{m.S:x2}{m.R.R:x2}{m.R.G:x2}{m.R.B:x2}";
            }
            private static Metalness.MaterialPoint StringToMaterial(ReadOnlySpan<char> s)
            {
                if (s.Length != 14) throw new Exception($"Invalid material string: {s.ToString()}");

                return new Metalness.MaterialPoint(
                    new SixLabors.ImageSharp.PixelFormats.Rgb24(
                        byte.Parse(s.Slice(0, 2), System.Globalization.NumberStyles.HexNumber),
                        byte.Parse(s.Slice(2, 2), System.Globalization.NumberStyles.HexNumber),
                        byte.Parse(s.Slice(4, 2), System.Globalization.NumberStyles.HexNumber)
                    ),
                    byte.Parse(s.Slice(6, 2), System.Globalization.NumberStyles.HexNumber),
                    new SixLabors.ImageSharp.PixelFormats.Rgb24(
                        byte.Parse(s.Slice(8, 2), System.Globalization.NumberStyles.HexNumber),
                        byte.Parse(s.Slice(10, 2), System.Globalization.NumberStyles.HexNumber),
                        byte.Parse(s.Slice(12, 2), System.Globalization.NumberStyles.HexNumber)
                    )
                );
            }
        }

        private sealed class StringParsingConverter<T> : JsonConverter<T>
            where T : notnull
        {
            private readonly Func<string, T> _parse;
            public StringParsingConverter(Func<string, T> parse) => _parse = parse;

            public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.String) throw new JsonException();
                return _parse(reader.GetString());
            }
            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.ToString());
            }
        }
    }
}
