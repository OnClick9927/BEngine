using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace BEngine;

internal sealed class TextureAtlasYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(TextureAtlas);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var data = rootDeserializer(typeof(TextureAtlasYamlData)) as TextureAtlasYamlData ??
                   throw new InvalidDataException("The TextureAtlas YAML document is empty.");
        return data.ToAsset();
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        if (value is not TextureAtlas atlas)
            throw new InvalidDataException($"Expected {nameof(TextureAtlas)}, received {value?.GetType().FullName}.");
        serializer(TextureAtlasYamlData.FromAsset(atlas), typeof(TextureAtlasYamlData));
    }
}
