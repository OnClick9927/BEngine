using System.Runtime.CompilerServices;
using BEngine.UIElements;
namespace BEngine.Editor;
internal static class UIElementsEditorRegistration
{
    [ModuleInitializer] internal static void Register()
    {
        AssetTypeRegistry.Register<SdfFontAsset>(".sdfont", "SDF Font", context =>
            SdfFontAsset.FromBytes(RuntimeServiceAccess.FileSystem.ReadAllBytes(context.SourcePath), context.SourcePath), EditorBuiltinIcons.Assets.Font);
        AssetTypeRegistry.Register<VisualTreeAsset>(".bxml", "UI Document", context => VisualTreeAsset.Load(context.SourcePath), EditorBuiltinIcons.Assets.Markup);
        AssetTypeRegistry.Register<StyleSheet>(".bss", "UI Style Sheet", context => StyleSheet.Load(context.SourcePath), EditorBuiltinIcons.Assets.Style);
        AssetTypeRegistry.Register<StyleSheet>(".bcss", "UI Style Sheet", context => StyleSheet.Load(context.SourcePath), EditorBuiltinIcons.Assets.Style);
    }
}
