using BEngine.Editor;
using InspectorEditor = BEngine.Editor.Editor;

namespace BEngine.ExampleTests.AssetPreview;

internal sealed class PreviewProtocolProbeAsset : ScriptableObject;

[CustomEditor(typeof(PreviewProtocolProbeAsset))]
internal sealed class PreviewProtocolProbeEditor : InspectorEditor
{
    internal const string PreviewSentinel = "CUSTOM_PREVIEW_GUI_SENTINEL";
    internal const string InfoSentinel = "CUSTOM_PREVIEW_INFO_SENTINEL";
    internal static int PreviewCalls { get; private set; }
    internal static int DisableCalls { get; private set; }

    public override void OnInspectorGUI() => GUILayout.Label("Custom preview protocol probe");
    public override bool HasPreviewGUI() => true;

    public override void OnPreviewGUI(Rect previewArea)
    {
        PreviewCalls++;
        GUI.DrawRect(previewArea, new Color(Fix64.FromDecimal(0.82m), Fix64.FromDecimal(0.17m),
            Fix64.FromDecimal(0.64m), 1));
        GUI.Label(previewArea, PreviewSentinel, EditorStyles.boldLabel);
    }

    public override string GetInfoString() => InfoSentinel;
    protected override void OnDisable() => DisableCalls++;

    internal static void Reset()
    {
        PreviewCalls = 0;
        DisableCalls = 0;
    }
}

internal sealed class FaultingPreviewProbeAsset : ScriptableObject;

[CustomEditor(typeof(FaultingPreviewProbeAsset))]
internal sealed class FaultingPreviewProbeEditor : InspectorEditor
{
    internal const string InfoSentinel = "FAULTING_PREVIEW_INFO_SENTINEL";
    internal static int PreviewCalls { get; private set; }
    internal static int DisableCalls { get; private set; }

    public override void OnInspectorGUI() => GUILayout.Label("Faulting preview protocol probe");
    public override bool HasPreviewGUI() => true;

    public override void OnPreviewGUI(Rect previewArea)
    {
        PreviewCalls++;
        throw new InvalidOperationException("FAULTING_PREVIEW_GUI_SENTINEL");
    }

    public override string GetInfoString() => InfoSentinel;
    protected override void OnDisable() => DisableCalls++;

    internal static void Reset()
    {
        PreviewCalls = 0;
        DisableCalls = 0;
    }
}
