using System.Runtime.CompilerServices;
namespace BEngine;
internal sealed class TextureAtlasSourceProvider : ISpriteSourceProvider
{
    public bool TryResolve(Sprite sprite, out SpriteRenderData2D data)
    {
        return TextureAtlasResolver.TryResolve(sprite, out data);
    }
    public Sprite? LoadReference(string reference, IReadOnlyDictionary<string, string>? context) =>
        context is not null && context.TryGetValue("atlas", out var source) && !string.IsNullOrWhiteSpace(source)
            ? TextureAtlasResolver.LoadSpriteReference(reference, source) : null;
    public void Invalidate() => TextureAtlasResolver.Clear();
}
internal static class TextureAtlasRegistration
{
    internal static readonly TextureAtlasYamlConverter Converter = new();

    [ModuleInitializer] internal static void Initialize()
    {
        YamlUtility.RegisterConverter(Converter);
        SpriteSourceRegistry.Register(new TextureAtlasSourceProvider());
        RuntimeAssetCodecRegistry.Register<TextureAtlas>((bytes, _) => TextureAtlasRuntimeData.Deserialize(bytes.Span));
    }
}
