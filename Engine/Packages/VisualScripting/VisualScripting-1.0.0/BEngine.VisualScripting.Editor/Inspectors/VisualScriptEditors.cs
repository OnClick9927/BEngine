using BEngine.Editor;

namespace BEngine.VisualScripting.Editor;

[CustomEditor(typeof(VisualScriptGraph))]
public sealed class VisualScriptGraphEditor : BAssetEditor
{
    public override void OnInspectorGUI()
    {
        var graph = (VisualScriptGraph)target;
        GUILayout.Label($"{graph.nodes.Count} nodes, {graph.connections.Count} connections, {graph.variables.Count} variables");
        if (GUILayout.Button("Open Visual Script")) VisualScriptWindow.Open(graph);
    }
}

[CustomEditor(typeof(VisualScriptRunner))]
public sealed class VisualScriptRunnerEditor : BEngine.Editor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var runner = (VisualScriptRunner)target;
        GUILayout.Label($"Status: {runner.status}");
        if (runner.status == VisualScriptExecutionStatus.Paused)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Continue")) runner.ContinueExecution();
            if (GUILayout.Button("Step")) runner.StepExecution();
            if (GUILayout.Button("Stop")) runner.StopExecution();
            GUILayout.EndHorizontal();
        }
        if (runner.graph is not null && GUILayout.Button("Open Graph")) VisualScriptWindow.Open(runner.graph);
    }
}
