using BEngine.Editor;
using BEngine.Rendering.Rhi;

namespace BEngine.ShaderGraph.Editor;

[CustomEditor(typeof(ShaderGraphMaterial))]
public sealed class ShaderGraphMaterialEditor : BAssetEditor
{
    public override void OnInspectorGUI()
    {
        var material = (ShaderGraphMaterial)target;
        ShaderGraphAsset? graph;
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
        {
            graph = EditorGUILayout.ObjectField("Graph", material.graph, false);
            if (!ReferenceEquals(graph, material.graph)) Change("Change material graph", () => material.graph = graph);
        }
        if (graph is null) return;
        if (GUILayout.Button("Open Shader Graph")) ShaderGraphWindow.Open(graph);
        if (GUILayout.Button("GPU Preview")) ShaderGraphPreviewWindow.Open(graph).SetMaterial(material);
        CompiledShaderGraph compiled;
        try { compiled = ShaderGraphCompiler.Compile(graph, GraphicsBackend.Vulkan); }
        catch (Exception exception) { EditorGUILayout.HelpBox(exception.Message, MessageType.Error); return; }
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
        {
            foreach (var parameter in compiled.Parameters)
            {
                var value = material.parameters.FirstOrDefault(item => item.name == parameter.Name)?.value ?? parameter.DefaultValue;
                var next = EditorGUILayout.ColorField(parameter.Name, value);
                if (!value.Equals(next)) Change("Change material parameter", () => material.SetParameter(parameter.Name, next));
            }
            foreach (var parameter in compiled.Textures)
            {
                var entry = material.textures.FirstOrDefault(item => item.name == parameter.Name);
                var value = entry is null ? parameter.DefaultTexture : entry.texture;
                var next = EditorGUILayout.ObjectField(parameter.Name, value, false);
                if (!ReferenceEquals(value, next)) Change("Change material texture", () => material.SetTexture(parameter.Name, next));
            }
            if (GUILayout.Button("Reset overrides")) Change("Reset material overrides", () => { material.parameters.Clear(); material.textures.Clear(); });
        }
    }

    private void Change(string label, Action action)
    {
        EditorAssetWritePolicy.EnsureCanWrite(label);
        Undo.IncrementCurrentGroup(); Undo.RecordObject(target, label); action(); EditorUtility.SetDirty(target);
    }
}
