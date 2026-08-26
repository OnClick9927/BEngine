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
            VerifySkinOwnedEditorStylesAndToggleStates();
            VerifyFixedFontDensity();
            VerifyControlStates();
            VerifySegmentedButtonsAndDropDownApi();
            VerifyScaledTextCaret();
            Console.WriteLine("UNITY_EDITOR_THEME_OK|palette,skin-owned-styles,toggle-states,fixed-14px-font,density,alignment,focus," +
                              "disabled,segmented-button-background,dropdown-api,scaled-caret");
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

    private static void VerifySkinOwnedEditorStylesAndToggleStates()
    {
        var dark = EditorAppearance.builtInSkins.Single(skin => skin.name == nameof(EditorTheme.Dark));
        try
        {
            foreach (var skin in EditorAppearance.builtInSkins)
            {
                EditorAppearance.SetSkin(skin);
                Require(ReferenceEquals(EditorStyles.centeredBoldLabel, skin.centeredBoldLabel) &&
                        ReferenceEquals(EditorStyles.centeredMiniLabel, skin.centeredMiniLabel),
                    $"{skin.name} does not own the centered editor label styles.");
                Require(skin.toggle.normal.backgroundColor.Equals(skin.palette.Field) &&
                        skin.toggle.onNormal.backgroundColor.Equals(skin.palette.Field) &&
                        skin.toggle.normal.borderColor.Equals(skin.palette.Border) &&
                        skin.toggle.onNormal.textColor.Equals(skin.palette.Text),
                    $"{skin.name} toggle off/on states do not follow its palette.");

                foreach (var value in new[] { false, true })
                {
                    var commands = Render(() => GUI.Toggle(new Rect(10, 8, 220, 30), value, "Visible"));
                    Require(commands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                                    command.Color == GpuCanvasColor.FromColor(skin.palette.Field)) &&
                            commands.Count(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                                      command.Color == GpuCanvasColor.FromColor(
                                                          skin.palette.Border)) >= 4,
                        $"{skin.name} {(value ? "checked" : "unchecked")} toggle has no visible field or border.");
                }
            }
        }
        finally
        {
            EditorAppearance.SetSkin(dark);
        }
    }

    private static void VerifyFixedFontDensity()
    {
        Require(EditorAppearance.fontSize == EditorAppearance.DefaultFontSize &&
                GUI.skin.label.fontSize == EditorAppearance.DefaultFontSize,
            "A preference changed the fixed 14px editor font.");
        Require(EditorGUIUtility.singleLineHeight >= EditorAppearance.DefaultFontSize + 4,
            $"14px editor text is clipped by a {EditorGUIUtility.singleLineHeight}px field.");
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

    private static void VerifySegmentedButtonsAndDropDownApi()
    {
        Require(EditorStyles.toolbarButton.normal.backgroundColor.Equals(EditorAppearance.palette.Toolbar) &&
                EditorStyles.toolbarButton.borderWidth == Fix64.Zero,
            "Toolbar button colors were not restored to the toolbar surface.");
        Require(EditorStyles.toolbarButton.normal.backgroundImage is not null &&
                GUI.skin.button.normal.backgroundImage is not null &&
                EditorStyles.dropDownButton.normal.backgroundImage is not null,
            "Button GUIStyles do not expose their textured background image.");

        var toolbar = Render(() => GUI.Button(new Rect(10, 8, 120, 28), "Toolbar",
            EditorStyles.toolbarButton));
        Require(toolbar.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                      command.Color == GpuCanvasColor.FromColor(EditorAppearance.palette.Border) &&
                                      Math.Abs(command.Rect.X - 129) < .01f &&
                                      Math.Abs(command.Rect.Width - 1) < .01f),
            "Toolbar button texture did not produce its right-side separator.");

        var dropDown = Render(() => EditorGUI.DropDownButton(new Rect(10, 8, 220, 30),
            new GUIContent("Options"), FocusType.Keyboard));
        var label = dropDown.Single(command => command.Type == GpuCanvasCommandType.Text &&
                                                 command.Content == "Options");
        var arrow = dropDown.Single(command => command.Type == GpuCanvasCommandType.Image &&
                                                 command.Content.EndsWith("FoldoutOpen.png",
                                                     StringComparison.OrdinalIgnoreCase));
        Require(arrow.Rect.X > label.Rect.X && arrow.Rect.X >= 210 &&
                dropDown.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                        command.Color == GpuCanvasColor.FromColor(
                                            EditorAppearance.palette.Border) &&
                                        Math.Abs(command.Rect.X - 210) < .01f),
            "DropDownButton does not visibly separate its dropdown arrow region.");

        var clicked = false;
        Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(40, 18), button = 0 },
            () => clicked |= EditorGUI.DropDownButton(new Rect(10, 8, 220, 30), "Options",
                FocusType.Keyboard));
        Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(40, 18), button = 0 },
            () => clicked |= EditorGUI.DropDownButton(new Rect(10, 8, 220, 30), "Options",
                FocusType.Keyboard));
        Require(clicked, "DropDownButton did not report a complete click.");

        GUIUtility.keyboardControl = 0;
        Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(40, 18), button = 0 },
            () => EditorGUI.DropDownButton(new Rect(10, 8, 220, 30), "Passive",
                FocusType.Passive));
        Require(GUIUtility.keyboardControl == 0,
            "A Passive DropDownButton incorrectly captured keyboard focus.");
        Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(40, 18), button = 0 },
            () => EditorGUI.DropDownButton(new Rect(10, 8, 220, 30), "Passive",
                FocusType.Passive));

        var layout = Render(() => EditorGUILayout.DropDownButton(new GUIContent("Layout Options"),
            FocusType.Keyboard, GUILayout.Width(180)));
        Require(layout.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                      command.Content == "Layout Options") &&
                layout.Any(command => command.Type == GpuCanvasCommandType.Image &&
                                      command.Content.EndsWith("FoldoutOpen.png",
                                          StringComparison.OrdinalIgnoreCase)),
            "EditorGUILayout.DropDownButton did not render its content and dropdown affordance.");
    }

    private static void VerifyScaledTextCaret()
    {
        const string text = "WiWi scale";
        const int boundary = 3;
        var previousScale = GUIUtility.pixelsPerPoint;
        GUIUtility.keyboardControl = 0;
        GUIUtility.hotControl = 0;
        GUIUtility.pixelsPerPoint = Fix64.FromDecimal(1.5m);
        try
        {
            var field = new Rect(10, 8, 260, 30);
            var prefixWidth = EditorStyles.textField.CalcSize(new GUIContent(text[..boundary])).x;
            var logicalPointer = new Vector2(field.x + 4 + prefixWidth, field.y + field.height / 2);
            var physicalPointer = logicalPointer * GUIUtility.pixelsPerPoint;
            Dispatch(new Event(EventType.MouseDown) { mousePosition = physicalPointer, button = 0 },
                () => GUI.TextField(field, text, style: EditorStyles.textField), width: 520, height: 120);
            var commands = Render(() => GUI.TextField(field, text, style: EditorStyles.textField),
                width: 520, height: 120);
            var caret = commands.Single(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                                   command.Color == GpuCanvasColor.FromColor(
                                                       EditorAppearance.palette.Text) &&
                                                   Math.Abs(command.Rect.Width - 1.5f) < .01f &&
                                                   command.Rect.Height > 30);
            var expectedX = (float)((field.x + 4 + prefixWidth) * GUIUtility.pixelsPerPoint);
            Require(Math.Abs(caret.Rect.X - expectedX) <= .75f,
                $"Scaled text caret is detached from the measured glyph boundary: " +
                $"actual={caret.Rect.X:0.##}, expected={expectedX:0.##}.");
        }
        finally
        {
            GUIUtility.keyboardControl = 0;
            GUIUtility.hotControl = 0;
            GUIUtility.pixelsPerPoint = previousScale;
        }
    }

    private static List<GpuCanvasCommand> Render(Action draw, int width = 260, int height = 60)
    {
        var commands = new List<GpuCanvasCommand>();
        Dispatch(new Event(EventType.Repaint), draw, commands, width, height);
        return commands;
    }

    private static void Dispatch(Event evt, Action draw, List<GpuCanvasCommand>? commands = null,
        int width = 260, int height = 60)
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
