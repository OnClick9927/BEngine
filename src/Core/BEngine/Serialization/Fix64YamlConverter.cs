using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace BEngine;

/// <summary>Stores fixed-point values as exact decimal scalars, including their last raw bit.</summary>
internal sealed class Fix64YamlConverter : IYamlTypeConverter
{
    internal static readonly Fix64YamlConverter Instance = new();

    public bool Accepts(Type type) => type == typeof(Fix64) || type == typeof(Fix64?);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
        {
            if (type == typeof(Fix64?) && scalar.Value is "" or "~" or "null" or "Null" or "NULL") return null;
            if (Fix64.TryParse(scalar.Value, CultureInfo.InvariantCulture, out var value)) return value;
            throw new YamlException(scalar.Start, scalar.End, $"'{scalar.Value}' is not a valid Fix64 value.");
        }

        // Earlier serializers emitted the public RawValue property as a mapping.
        // Read those assets exactly while writing only the scalar representation.
        var start = parser.Consume<MappingStart>();
        long? raw = null;
        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>();
            var value = parser.Consume<Scalar>();
            if (key.Value is not ("rawValue" or "RawValue") || raw.HasValue ||
                !long.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                throw new YamlException(key.Start, value.End, "A legacy Fix64 mapping must contain exactly one integer rawValue.");
            raw = parsed;
        }
        return raw.HasValue ? Fix64.FromRaw(raw.Value)
            : throw new YamlException(start.Start, start.End, "A legacy Fix64 mapping requires rawValue.");
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        if (value is Fix64 number) emitter.Emit(new Scalar(number.ToString()));
        else if (value is null && type == typeof(Fix64?)) emitter.Emit(new Scalar("null"));
        else throw new InvalidDataException($"Expected {nameof(Fix64)}, received {value?.GetType().FullName}.");
    }
}
