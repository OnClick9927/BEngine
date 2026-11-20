using System.Runtime.CompilerServices;
namespace BEngine.UIElements;
internal static class UIElementsAssetRegistration
{
    [ModuleInitializer] internal static void Register()
    {
        RuntimeAssetCodecRegistry.Register<SdfFontAsset>((bytes, path) => SdfFontAsset.FromBytes(bytes.Span, path));
        RuntimeAssetCodecRegistry.Register<VisualTreeAsset>((bytes, path) =>
        {
            var text = RuntimeAssetDecoding.DecodeText(bytes.Span);
            return path.EndsWith(".bxml", StringComparison.OrdinalIgnoreCase)
                ? VisualTreeAsset.FromUxml(text, path)
                : VisualTreeAsset.FromDocument(YamlUtility.Deserialize<UIAssetDocument>(text), path);
        });
        RuntimeAssetCodecRegistry.Register<StyleSheet>((bytes, _) => StyleSheet.Parse(RuntimeAssetDecoding.DecodeText(bytes.Span)));
    }
}
