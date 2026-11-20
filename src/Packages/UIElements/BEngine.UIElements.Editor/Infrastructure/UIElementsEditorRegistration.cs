using System.Runtime.CompilerServices;
using BEngine.UIElements;
namespace BEngine.Editor;
internal static class UIElementsEditorRegistration
{
    [ModuleInitializer] internal static void Register()
    {
        PlayerResourceTextFormats.Register(UIElementsResourceTypeNames.Read, ".bxml", ".bss", ".bcss", ".xml");
        AssetTypeRegistry.Register<SdfFontAsset>(".sdfont", "SDF Font", context =>
            SdfFontAsset.FromBytes(RuntimeServices.GetRequiredService<global::BEngine.Platform.FileUtility>().ReadAllBytes(context.SourcePath), context.SourcePath), EditorBuiltinIcons.Assets.Font);
        PlatformAssetBuildPipeline.RegisterRuntimeCompiler(".sdfont", "SDF Font", "sdf-binary-v1",
            192 * 1024 * 1024, static (bytes, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                return SdfFontAsset.CompileRuntimeBytes(bytes.Span);
            });
        AssetTypeRegistry.Register<VisualTreeAsset>(".bxml", "UI Document", context => VisualTreeAsset.Load(context.SourcePath), EditorBuiltinIcons.Assets.Markup, null, LoadTextFallback);
        AssetTypeRegistry.Register<StyleSheet>(".bss", "UI Style Sheet", context => StyleSheet.Load(context.SourcePath), EditorBuiltinIcons.Assets.Style, null, LoadTextFallback);
        AssetTypeRegistry.Register<StyleSheet>(".bcss", "UI Style Sheet", context => StyleSheet.Load(context.SourcePath), EditorBuiltinIcons.Assets.Style, null, LoadTextFallback);
    }

    private static BAsset LoadTextFallback(AssetLoadContext context) => new TextAsset(
        EditorServices.GetRequiredService<BEngine.Platform.FileUtility>().ReadAllText(context.ImportedPath));
}
