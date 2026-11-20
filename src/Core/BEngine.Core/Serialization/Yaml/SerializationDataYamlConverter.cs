using BEngine.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace BEngine;

/// <summary>Preserves the adapter's existing mapping shape, without embedding an extra wrapper node.</summary>
internal sealed class SerializationDataYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => SerializationDataAdapter.Find(type) is not null;
    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var adapter = SerializationDataAdapter.Find(type)!;
        var data = rootDeserializer(adapter.DataType);
        return data is null ? null : adapter.RestoreData(data);
    }
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var adapter = SerializationDataAdapter.Find(type)!;
        serializer(value is null ? null : adapter.CaptureData(value), adapter.DataType);
    }
}
