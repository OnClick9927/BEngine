using System.Runtime.CompilerServices;
namespace BEngine.UIElements;
internal static class UIElementsAssetRegistration
{
    [ModuleInitializer] internal static void Register()
    {
        RuntimeAssetCodecRegistry.Register<SdfFontAsset>((bytes, path) => SdfFontAsset.FromBytes(bytes.Span, path));
        RuntimeAssetCodecRegistry.Register<VisualTreeAsset>((bytes, path) =>
        {
            var text = TextAsset.Decode(bytes.ToArray());
            return path.EndsWith(".bxml", StringComparison.OrdinalIgnoreCase)
                ? VisualTreeAsset.FromUxml(text, path)
                : VisualTreeAsset.FromDocument(YamlUtility.Deserialize<UIAssetDocument>(text), path);
        });
        RuntimeAssetCodecRegistry.Register<StyleSheet>((bytes, _) => StyleSheet.Parse(TextAsset.Decode(bytes.ToArray())));
    }
}
