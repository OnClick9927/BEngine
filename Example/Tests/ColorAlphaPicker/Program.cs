using System.Reflection;
using BEngine;
using BEngine.Editor;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.ColorAlphaPicker;

internal static class Program
{
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod("BeginFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod("EndFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private static int Main()
    {
        try
        {
            VerifyGpuImGuiBoundary();
            VerifyAlphaPicker();
            Console.WriteLine("COLOR_ALPHA_PICKER_OK|gpu-imgui,rgba-slider,hex,alpha-bar,no-uxml");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifyGpuImGuiBoundary()
    {
        var editorAssembly = typeof(EditorGUI).Assembly;
        Require(editorAssembly.GetReferencedAssemblies().All(reference =>
                !string.Equals(reference.Name, "BEngine.UIElements", StringComparison.Ordinal)),
            "BEngine.Editor still depends on the optional UIElements assembly.");
        Require(editorAssembly.GetManifestResourceNames().All(name =>
                !name.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase)),
            "BEngine.Editor still embeds a UXML color picker.");
    }

    private static void VerifyAlphaPicker()
    {
        var initial = new Color(Fix64.FromDecimal(0.125m), Fix64.FromDecimal(0.25m),
            Fix64.FromDecimal(0.5m), Fix64.FromDecimal(0.375m));
        var value = initial;
        var field = new Rect(0, 0, 320, 18);

        var fieldCommands = Render(320, 40, () => value = EditorGUI.ColorField(field, "Tint", value));
        Require(fieldCommands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                             command.Color == GpuCanvasColor.FromColor(
                                                 new Color(initial.r, initial.g, initial.b, 1))),
            "GPU IMGUI ColorField did not render the selected RGB color.");
        Require(fieldCommands.Count(command => command.Type == GpuCanvasCommandType.SolidRect) >= 6,
            "GPU IMGUI ColorField did not render its Alpha bar and eyedropper chrome.");

        Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(220, 9), button = 0 }, 320, 40,
            () => value = EditorGUI.ColorField(field, "Tint", value));
        Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(220, 9), button = 0 }, 320, 40,
            () => value = EditorGUI.ColorField(field, "Tint", value));

        var picker = EditorWindow.focusedWindow;
        Require(picker?.GetType().Name == "EditorColorPickerWindow",
            "Clicking the GPU IMGUI swatch did not open EditorColorPickerWindow.");
        var onGui = picker!.GetType().GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pickerCommands = Render(360, 480, () => onGui.Invoke(picker, null));
        Require(pickerCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                              command.Content is "Alpha" or "A"),
            "The GPU color picker does not expose an Alpha slider.");
        Require(pickerCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                              command.Content.TrimStart('#').Length == 6 &&
                                              command.Content.TrimStart('#').All(Uri.IsHexDigit)),
            "The GPU color picker does not display RGB hexadecimal output.");

        Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(250, 318), button = 0 }, 360, 480,
            () => onGui.Invoke(picker, null));
        Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(250, 318), button = 0 }, 360, 480,
            () => onGui.Invoke(picker, null));
        Render(320, 40, () => value = EditorGUI.ColorField(field, "Tint", value));
        Require(value.a > initial.a,
            $"Dragging the GPU Alpha slider did not update the fixed-point color: {value.a}.");
        picker.Close();
    }

    private static List<GpuCanvasCommand> Render(int width, int height, Action draw)
    {
        var commands = new List<GpuCanvasCommand>();
        Dispatch(new Event(EventType.Repaint), width, height, draw, commands);
        return commands;
    }

    private static void Dispatch(Event evt, int width, int height, Action draw,
        List<GpuCanvasCommand>? commands = null)
    {
        BeginFrame.Invoke(null, [evt, width, height, commands ?? []]);
        try { draw(); }
        finally { EndFrame.Invoke(null, null); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
