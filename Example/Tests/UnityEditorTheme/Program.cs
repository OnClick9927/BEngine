using System.Reflection;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.UnityEditorTheme;

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
            EditorAppearance.Apply(new EditorPreferencesDocument
            {
                EditorTheme = "Dark",
                EditorFont = "Microsoft YaHei UI",
                EditorFontSize = 20
            });
            VerifyPaletteHierarchy();
            VerifyFontAwareDensity();
            VerifyControlStates();
            Console.WriteLine("UNITY_EDITOR_THEME_OK|palette,density,alignment,focus,disabled");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            GUI.enabled = true;
            EditorAppearance.Apply(new EditorPreferencesDocument());
        }
    }

    private static void VerifyPaletteHierarchy()
    {
        var palette = EditorAppearance.palette;
        Require(!palette.Window.Equals(palette.Panel) && !palette.Panel.Equals(palette.PanelRaised) &&
                !palette.Toolbar.Equals(palette.TitleBar) && !palette.Field.Equals(palette.Button),
            "Unity-style chrome, content, raised panels, fields and buttons are not visually distinct.");
        Require(!palette.Selection.Equals(palette.SelectionInactive) &&
                !palette.FocusBorder.Equals(palette.Border),
            "Focused and inactive selection states use the same color.");
        Require(!palette.Text.Equals(palette.MutedText) && !palette.MutedText.Equals(palette.DisabledText),
            "Normal, muted and disabled text do not have a readable hierarchy.");
    }

    private static void VerifyFontAwareDensity()
    {
        Require(EditorGUIUtility.singleLineHeight >= 27,
            $"20px editor text is clipped by a {EditorGUIUtility.singleLineHeight}px field.");
        Require(EditorStyles.treeViewRow.fixedHeight >= EditorGUIUtility.singleLineHeight &&
                EditorStyles.dockTab.fixedHeight > EditorGUIUtility.singleLineHeight &&
                EditorStyles.toolbar.fixedHeight > EditorGUIUtility.singleLineHeight,
            "Tree rows, tabs or toolbars do not follow the actual font line height.");

        var commands = Render(() => GUI.Button(new Rect(10, 8, 220, 34), "Centered Command"));
        var text = commands.Single(command => command.Type == GpuCanvasCommandType.Text &&
                                              command.Content == "Centered Command");
        var leftInset = text.Rect.X - 10;
        var rightInset = 230 - text.Rect.Right;
        Require(Math.Abs(leftInset - rightInset) <= 1,
            $"Unity-style command button text is not centered inside its control: {text.Rect}.");
    }

    private static void VerifyControlStates()
    {
        Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(40, 18), button = 0 },
            () => GUI.TextField(new Rect(10, 8, 220, 30), "Focused"));
        Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(40, 18), button = 0 },
            () => GUI.TextField(new Rect(10, 8, 220, 30), "Focused"));
        var focused = Render(() => GUI.TextField(new Rect(10, 8, 220, 30), "Focused"));
        Require(focused.Count(command => command.Type == GpuCanvasCommandType.SolidRect &&
                         command.Color == GpuCanvasColor.FromColor(EditorAppearance.palette.FocusBorder)) >= 4,
            "Focused text field did not render the Unity-style one-pixel focus border.");

        GUI.enabled = false;
        var disabled = Render(() => GUI.Button(new Rect(10, 8, 220, 30), "Disabled Command"));
        GUI.enabled = true;
        Require(disabled.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                        command.Color == GpuCanvasColor.FromColor(
                                            EditorAppearance.palette.DisabledText)),
            "Disabled controls do not use the shared disabled text color.");
    }

    private static List<GpuCanvasCommand> Render(Action draw)
    {
        var commands = new List<GpuCanvasCommand>();
        Dispatch(new Event(EventType.Repaint), draw, commands);
        return commands;
    }

    private static void Dispatch(Event evt, Action draw, List<GpuCanvasCommand>? commands = null)
    {
        BeginFrame.Invoke(null, [evt, 260, 60, commands ?? []]);
        try { draw(); }
        finally { EndFrame.Invoke(null, null); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
