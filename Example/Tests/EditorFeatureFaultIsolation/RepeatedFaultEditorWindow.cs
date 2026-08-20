using System.Runtime.CompilerServices;
using BEngine;
using BEngine.Editor;

namespace BEngine.ExampleTests.EditorFeatureFaultIsolation;

internal sealed class RepeatedFaultEditorWindow : EditorWindow
{
    internal int GuiCalls { get; private set; }

    protected override void OnGUI()
    {
        GuiCalls++;
        GUI.enabled = false;
        GUILayout.BeginHorizontal();
        GUI.BeginScrollView(new Rect(2, 2, 180, 80), Vector2.zero,
            new Rect(0, 0, 360, 240));
        ThrowFromRepeatedWindowOnGui();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromRepeatedWindowOnGui() =>
        throw new InvalidOperationException("REPEATED_WINDOW_GUI_SENTINEL");
}
