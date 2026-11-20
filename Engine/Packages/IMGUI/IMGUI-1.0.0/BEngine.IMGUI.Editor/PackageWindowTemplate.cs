using BEngine.ProjectSystem;
namespace BEngine.Editor;
internal static class PackageWindowTemplate
{
    internal static PackageEditorTemplate Create(PackageEditorTemplateContext context) => new($$"""
        using BEngine.Editor;
        namespace {{context.SourceNamespace}}.Editor;
        public sealed class {{context.WindowName}} : EditorWindow<{{context.WindowName}}>
        {
            [MenuItem("Window/{{context.Name}}")]
            private static void Open() => GetWindow<{{context.WindowName}}>().Show();
            protected override void OnGUI() => GUILayout.Label("{{context.Name}} is ready.");
        }
        """, ["BEngine.IMGUI.Editor"], [new() { PackageId = "com.bengine.imgui", Target = "editor" }]);
}
