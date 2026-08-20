using System.Runtime.CompilerServices;
using BEngine;
using BEngine.Editor;

namespace BEngine.ExampleTests.EditorFeatureFaultIsolation;

[CustomPropertyDrawer(typeof(FaultingPropertyAttribute))]
internal sealed class FaultingPropertyDrawer : PropertyDrawer
{
    internal static int OnGuiCalls { get; private set; }
    internal static int HeightCalls { get; private set; }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        OnGuiCalls++;
        GUI.enabled = false;
        GUILayout.BeginHorizontal();
        GUI.BeginScrollView(position, Vector2.zero,
            new Rect(0, 0, Fix64.Max(position.width, 320), Fix64.Max(position.height, 120)));
        ThrowFromPropertyDrawerOnGui();
    }

    public override Fix64 GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        HeightCalls++;
        return ThrowFromPropertyDrawerHeight();
    }

    internal static void Reset()
    {
        OnGuiCalls = 0;
        HeightCalls = 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromPropertyDrawerOnGui() =>
        throw new InvalidOperationException("PROPERTY_DRAWER_GUI_SENTINEL");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Fix64 ThrowFromPropertyDrawerHeight() =>
        throw new InvalidOperationException("PROPERTY_DRAWER_HEIGHT_SENTINEL");
}
