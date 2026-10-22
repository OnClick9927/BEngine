using BEngine.Rendering;

namespace BEngine.Editor;

public sealed class TextureStreamingWindow : EditorWindow
{
    [MenuItem("Window/Analysis/Texture Streaming", false, 216)]
    public static void Open() => GetWindow<TextureStreamingWindow>("Texture Streaming").Show();
    protected override void OnEnable() => minSize = new(320, 200);
    protected override void Update() { if (TextureStreaming.enabled) Repaint(); }
    protected override void OnGUI()
    {
        EditorGUI.BeginChangeCheck();
        var enabled = EditorGUILayout.Toggle("Enable Streaming", TextureStreaming.enabled);
        var megabytes = EditorGUILayout.IntField("Budget (MiB)", (int)Math.Clamp(TextureStreaming.memoryBudgetBytes / (1024 * 1024), 1, 4096));
        var bias = EditorGUILayout.IntField("Mip Bias", TextureStreaming.mipBias);
        if (EditorGUI.EndChangeCheck())
        {
            TextureStreaming.enabled = enabled;
            TextureStreaming.memoryBudgetBytes = (long)Math.Clamp(megabytes, 1, 4096) * 1024 * 1024;
            TextureStreaming.mipBias = Math.Clamp(bias, -8, 8);
        }
        GUILayout.Label($"Resident payload: {TextureStreaming.residentMemoryBytes / 1048576d:0.00} MiB", EditorStyles.boldLabel);
        GUILayout.Label($"Explicit requests: {TextureStreaming.explicitRequestCount}");
        GUILayout.Label($"Projected requests: {TextureStreaming.projectedRequestCount} / {TextureStreaming.MaximumProjectedRequests}");
        if (GUILayout.Button("Clear Requests")) TextureStreaming.ClearAllRequests();
    }
}
