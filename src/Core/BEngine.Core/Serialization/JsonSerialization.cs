using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BEngine.Serialization;

namespace BEngine;

/// <summary>JSON documents for engine data and types with serialization data adapters.</summary>
public static class JsonSerialization
{
    internal static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize<T>(T value) => Serialize(value, typeof(T));

    public static string Serialize(object? value, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return JsonSerializer.Serialize(value, type, Options);
    }

    public static T? Deserialize<T>(string json) => (T?)Deserialize(json, typeof(T));

    public static object? Deserialize(string json, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(type);
        return JsonSerializer.Deserialize(json, type, Options);
    }

    internal static JsonNode? ToNode(object? value, Type type) => JsonSerializer.SerializeToNode(value, type, Options);

    internal static object? FromNode(JsonNode? node, Type type) =>
        JsonSerializer.Deserialize(node?.ToJsonString() ?? "null", type, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(static info =>
        {
            if (info.Kind != JsonTypeInfoKind.Object) return;
            for (var index = info.Properties.Count - 1; index >= 0; index--)
            {
                var property = info.Properties[index];
                if (property.AttributeProvider is not MemberInfo member) continue;
                if (member.IsDefined(typeof(SerializationIgnoreAttribute), inherit: true))
                {
                    info.Properties.RemoveAt(index);
                    continue;
                }
                if (member.GetCustomAttribute<SerializationMemberAttribute>(inherit: true) is not { } setting)
                    continue;
                if (!string.IsNullOrWhiteSpace(setting.Alias)) property.Name = setting.Alias;
                else if (!setting.ApplyNamingConventions) property.Name = member.Name;
                property.Order = setting.Order;
            }
        });
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IncludeFields = true,
            TypeInfoResolver = resolver
        };
        options.Converters.Add(new Fix64JsonConverter());
        options.Converters.Add(new DataAdapterJsonConverterFactory());
        options.MakeReadOnly();
        return options;
    }

    private sealed class Fix64JsonConverter : JsonConverter<Fix64>
    {
        public override Fix64 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
                _ => null
            };
            return text is not null && Fix64.TryParse(text, CultureInfo.InvariantCulture, out var value)
                ? value : throw new JsonException("Invalid Fix64 value.");
        }

        public override void Write(Utf8JsonWriter writer, Fix64 value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    private sealed class DataAdapterJsonConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => SerializationDataAdapter.Find(typeToConvert) is not null;

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(DataAdapterJsonConverter<>).MakeGenericType(typeToConvert))!;

        private sealed class DataAdapterJsonConverter<T> : JsonConverter<T> where T : class
        {
            private readonly SerializationDataAdapter _adapter = SerializationDataAdapter.Find(typeof(T))!;

            public DataAdapterJsonConverter() { }

            public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var data = JsonSerializer.Deserialize(ref reader, _adapter.DataType, options);
                return data is null ? null : (T)_adapter.RestoreData(data);
            }

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
                JsonSerializer.Serialize(writer, _adapter.CaptureData(value), _adapter.DataType, options);
        }
    }
}
