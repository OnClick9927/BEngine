using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class DefaultEditorIconPolicy
{
    [ModuleInitializer]
    internal static void Register()
    {
        EditorIconRegistry.SetComponentFallback(ComponentFallback);
        EditorIconRegistry.SetTypeFallback(TypeFallback);
        EditorIconRegistry.Register(typeof(MonoScript), EditorBuiltinIcons.Assets.Script);
        EditorIconRegistry.Register(typeof(Scene), EditorBuiltinIcons.Assets.Scene);
        EditorIconRegistry.Register(typeof(PrefabAsset), EditorBuiltinIcons.Assets.Prefab);
    }
    private static string? TypeFallback(Type type) => typeof(Script).IsAssignableFrom(type)
        ? EditorBuiltinIcons.Assets.Script : typeof(ScriptableObject).IsAssignableFrom(type)
            ? EditorBuiltinIcons.Assets.Data : typeof(DefaultAsset).IsAssignableFrom(type)
                ? EditorBuiltinIcons.Assets.Default : type == typeof(TextAsset)
                    ? EditorBuiltinIcons.Assets.Text : null;
    private static string ComponentFallback(Type type) => typeof(Transform).IsAssignableFrom(type)
        ? EditorBuiltinIcons.Components.Transform : typeof(MonoBehaviour).IsAssignableFrom(type)
            ? EditorBuiltinIcons.Components.Script : EditorBuiltinIcons.Components.Default;
}
