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
            VerifyStyleHierarchy();
            VerifySkinOwnedEditorStylesAndToggleStates();
            VerifyFixedFontDensity();
            VerifyControlStates();
            VerifySegmentedButtonsAndDropDownApi();
            VerifyTextureStyleBackground();
            VerifyScaledTextCaret();
            Console.WriteLine("UNITY_EDITOR_THEME_OK|style-only-theme,skin-owned-styles,toggle-states,fixed-14px-font,density,alignment,focus," +
                              "disabled,segmented-button-background,texture-background,dropdown-api,scaled-caret");
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

    private static void VerifyStyleHierarchy()
    {
        var skin = GUI.skin;
        Require(!skin.window.normal.backgroundColor.Equals(skin.viewBackground.normal.backgroundColor) &&
                !skin.viewBackground.normal.backgroundColor.Equals(skin.frameBox.normal.backgroundColor) &&
                !skin.toolbar.normal.backgroundColor.Equals(skin.windowTitle.normal.backgroundColor) &&
                !skin.textField.normal.backgroundColor.Equals(skin.button.normal.backgroundColor),
            "Unity-style chrome, content, raised panels, fields and buttons are not visually distinct.");
        Require(!skin.selectionRect.normal.backgroundColor.Equals(skin.selectionRect.disabled.backgroundColor) &&
                !skin.textField.focused.borderColor.Equals(skin.textField.normal.borderColor),
            "Focused and inactive selection states use the same color.");
        Require(!skin.label.normal.textColor.Equals(skin.miniLabel.normal.textColor) &&
                !skin.miniLabel.normal.textColor.Equals(skin.label.disabled.textColor),
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
                Require(skin.toggle.normal.backgroundColor.Equals(skin.textField.normal.backgroundColor) &&
                        skin.toggle.onNormal.backgroundColor.Equals(skin.textField.normal.backgroundColor) &&
                        skin.toggle.normal.borderColor.Equals(skin.textField.normal.borderColor) &&
                        skin.toggle.onNormal.textColor.Equals(skin.label.normal.textColor),
                    $"{skin.name} toggle off/on states do not follow its GUIStyles.");

                foreach (var value in new[] { false, true })
                {
                    var commands = Render(() => GUI.Toggle(new Rect(10, 8, 220, 30), value, "Visible"));
                    Require(commands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                                    command.Color == GpuCanvasColor.FromColor(
                                                        skin.toggle.normal.backgroundColor)) &&
                            commands.Count(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                                      command.Color == GpuCanvasColor.FromColor(
                                                          skin.toggle.normal.borderColor)) >= 4,
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
                         command.Color == GpuCanvasColor.FromColor(
                             EditorStyles.textField.focused.borderColor)) >= 4,
            "Focused text field did not render the Unity-style one-pixel focus border.");

        GUI.enabled = false;
        var disabled = Render(() => GUI.Button(new Rect(10, 8, 220, 30), "Disabled Command"));
        GUI.enabled = true;
        Require(disabled.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                        command.Color == GpuCanvasColor.FromColor(
                                            GUI.skin.button.disabled.textColor)),
            "Disabled controls do not use the shared disabled text color.");
    }

    private static void VerifySegmentedButtonsAndDropDownApi()
    {
        Require(EditorStyles.toolbarButton.normal.backgroundColor.Equals(
                    EditorStyles.toolbar.normal.backgroundColor) &&
                EditorStyles.toolbarButton.borderWidth == Fix64.Zero,
            "Toolbar button colors were not restored to the toolbar surface.");
        Require(EditorStyles.toolbarButton.normal.backgroundImage is not null &&
                GUI.skin.button.normal.backgroundImage is not null &&
                EditorStyles.dropDownButton.normal.backgroundImage is not null,
            "Button GUIStyles do not expose their textured background image.");

        var toolbar = Render(() => GUI.Button(new Rect(10, 8, 120, 28), "Toolbar",
            EditorStyles.toolbarButton));
        Require(toolbar.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                      command.Color == GpuCanvasColor.FromColor(
                                          EditorStyles.toolbarButton.normal.borderColor) &&
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
                                            EditorStyles.dropDownButton.normal.borderColor) &&
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

    private static void VerifyTextureStyleBackground()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BEngine-StyleTexture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Panel.png");
            File.WriteAllBytes(path, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
            var texture = BAsset.Load<Texture>(path) ??
                          throw new InvalidOperationException("Could not load the GUIStyle Texture fixture.");
            var style = new GUIStyle(GUI.skin.box);
            style.normal.backgroundImage = texture;
            var commands = Render(() => GUI.Box(new Rect(10, 8, 120, 28), string.Empty, style));
            Require(commands.Any(command => command.Type == GpuCanvasCommandType.Image &&
                                            command.Content.Equals(texture.sourcePath,
                                                StringComparison.OrdinalIgnoreCase)),
                "A GUIStyleState Texture did not reach the GPU image command as its resolved source path.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void VerifyScaledTextCaret()
    {
        const string text = "Wi中文 scale";
        const int boundary = 3;
        var previousScale = GUIUtility.pixelsPerPoint;
        GUIUtility.keyboardControl = 0;
        GUIUtility.hotControl = 0;
        try
        {
            var field = new Rect(10, 8, 300, 30);
            var style = EditorStyles.textField;
            foreach (var value in new[] { .5m, .75m, 1m, 1.25m, 1.5m, 1.8m })
            {
                var scale = Fix64.FromDecimal(value);
                GUIUtility.pixelsPerPoint = scale;
                GUIUtility.keyboardControl = 0;
                GUIUtility.hotControl = 0;
                var edited = text;
                void Draw() => edited = GUI.TextField(field, edited, style: style);

                Dispatch(new Event(EventType.MouseDown)
                    {
                        mousePosition = new Vector2(field.x + 12, field.y + field.height / 2) * scale,
                        button = 0,
                        clickCount = 2
                    }, Draw, width: 760, height: 140);
                Dispatch(new Event(EventType.KeyDown) { keyCode = KeyCode.Home }, Draw,
                    width: 760, height: 140);
                var originCommands = Render(Draw, width: 760, height: 140);
                var originCaret = FindCaret(originCommands, style, scale);
                var textCommand = originCommands.Single(command => command.Type == GpuCanvasCommandType.Text &&
                                                                   command.Content == text);
                Require(Math.Abs(originCaret.Rect.X - textCommand.Rect.X) < .02f,
                    $"EditorScale {value:0.##} offsets an empty caret from the rendered text origin.");

                for (var index = 0; index < boundary; index++)
                    Dispatch(new Event(EventType.KeyDown) { keyCode = KeyCode.RightArrow }, Draw,
                        width: 760, height: 140);
                var boundaryCommands = Render(Draw, width: 760, height: 140);
                var boundaryCaret = FindCaret(boundaryCommands, style, scale);
                Require(boundaryCaret.Rect.X > originCaret.Rect.X,
                    $"EditorScale {value:0.##} did not advance the caret across ASCII/CJK text.");

                GUIUtility.keyboardControl = 0;
                Dispatch(new Event(EventType.MouseDown)
                    {
                        mousePosition = new Vector2((Fix64)boundaryCaret.Rect.X,
                            (field.y + field.height / 2) * scale),
                        button = 0,
                        clickCount = 1
                    }, Draw, width: 760, height: 140);
                Dispatch(new Event(EventType.KeyDown) { character = '|' }, Draw,
                    width: 760, height: 140);
                Require(edited == text.Insert(boundary, "|"),
                    $"EditorScale {value:0.##} clicked the wrong ASCII/CJK boundary: '{edited}'.");
            }
        }
        finally
        {
            GUIUtility.keyboardControl = 0;
            GUIUtility.hotControl = 0;
            GUIUtility.pixelsPerPoint = previousScale;
        }
    }

    private static GpuCanvasCommand FindCaret(
        IEnumerable<GpuCanvasCommand> commands,
        GUIStyle style,
        Fix64 scale)
    {
        var color = GpuCanvasColor.FromColor(style.focused.textColor);
        return commands.Single(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                          command.Color == color &&
                                          Math.Abs(command.Rect.Width - (float)scale) < .02f);
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
