using System.Reflection;
using BEngine.Documents;
using BEngine.Editor;
using BEngine.Editor.Rendering;
using BEngine.Rendering;
using BEngine.Serialization;

namespace BEngine.ExampleTests.GpuImGui;

internal static class Program
{
    private static string _text = "abcdef";

    private static int Main()
    {
        try
        {
            VerifyEventApi();
            VerifyGpuCommandsAndTextEditing();
            VerifyScaledTextCaretAndHitTesting();
            VerifyAlignedTextEditingAtScale();
            VerifyNumericEditingBuffers();
            VerifyFieldFocusIsolation();
            VerifyNativeMouseMoveClassification();
            VerifyWindowCoordinatesAndScrolling();
            VerifyEditorWindowRoutingAndDockTabs();
            EditorObjectPingTests.Run();
            VerifyProjectTreeVisualLayout();
            VerifyIconToolbarLanguage();
            VerifyPrefabWorkflow();
            VerifyAssemblyBoundary();
            Console.WriteLine("GPU_IMGUI_OK|event-current,layout,input,repaint,gpu-commands,caret,scaled-caret,aligned-text-editing,cjk-hit-testing,double-click,numeric-edit-buffer,field-focus-isolation,native-drag-routing,window-local-input,scroll,scrollbar-drag,scrollbar-release,dock-tabs,focus,mouse-over,border,object-ping,project-tree-row-clip,assets-packages-separator,icon-toolbar-separators,prefab,package-boundary,imgui-editor-boundary,editor-owned-infrastructure");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"GPU_IMGUI_FAILED|{exception}");
            return 1;
        }
    }

    private static void VerifyIconToolbarLanguage()
    {
        var commands = new List<GpuCanvasCommand>();
        GUI.BeginFrame(new Event(EventType.Repaint), 120, 40, commands);
        try
        {
            GUILayout.BeginHorizontal();
            EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Refresh, "Refresh", GUILayout.Width(24));
            EditorToolbar.IconToggle(true, EditorBuiltinIcons.Toolbar.Warning, "Warnings", GUILayout.Width(24));
            GUILayout.EndHorizontal();
        }
        finally { GUI.EndFrame(); }
        var images = commands.Where(item => item.Type == GpuCanvasCommandType.Image).ToArray();
        Require(images.Length == 2 && images.Any(item => item.Content.EndsWith("Refresh.png")) &&
                images.Any(item => item.Content.EndsWith("Warning.png")),
            "Icon toolbar did not emit the expected GPU image commands.");
        Require(!commands.Any(item => item.Type == GpuCanvasCommandType.Text &&
                                      item.Content is "Refresh" or "Warnings"),
            "Icon-only toolbar rendered tooltip text into the compact toolbar.");
        var surfaceColors = new HashSet<GpuCanvasColor>
        {
            GpuCanvasColor.FromColor(EditorStyles.toolbarIconButton.normal.backgroundColor),
            GpuCanvasColor.FromColor(EditorStyles.toolbarIconButton.hover.backgroundColor),
            GpuCanvasColor.FromColor(EditorStyles.toolbarIconButtonSelected.normal.backgroundColor)
        };
        foreach (var image in images)
            Require(commands.Any(item => item.Type == GpuCanvasCommandType.SolidRect &&
                                         surfaceColors.Contains(item.Color) &&
                                         item.Rect.X <= image.Rect.X && item.Rect.Y <= image.Rect.Y &&
                                         item.Rect.Right >= image.Rect.Right && item.Rect.Bottom >= image.Rect.Bottom),
                $"Icon toolbar button '{image.Content}' has no distinguishable surface.");
        var separator = GpuCanvasColor.FromColor(EditorStyles.toolbarIconButton.normal.borderColor);
        foreach (var image in images)
            Require(commands.Any(item => item.Type == GpuCanvasCommandType.SolidRect &&
                                         item.Color == separator && Math.Abs(item.Rect.Width - 1) < .01f &&
                                         item.Rect.X > image.Rect.Right && item.Rect.X - image.Rect.Right <= 6 &&
                                         item.Rect.Y <= image.Rect.Y && item.Rect.Bottom >= image.Rect.Bottom),
                $"Icon toolbar button '{image.Content}' has no vertical texture separator.");
    }

    private static void VerifyPrefabWorkflow()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BEngine-GpuImGui-Prefab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = new Scene("Source");
            var root = source.CreateGameObject("Robot");
            root.tag = "Player"; root.layer = 4; root.isStatic = true;
            root.transform.localPosition = new Vector2(1, 2);
            root.AddComponent<Camera2D>().size = 7;
            var child = source.CreateGameObject("Sensor");
            child.transform.SetParent(root.transform, false);
            child.AddComponent<SpriteRenderer>().opacity = Fix64.Parse("0.5");
            var path = Path.Combine(directory, "Robot.prefab.yaml");
            Document.SaveBObject<PrefabDocument>(root, path);
            var prefab = Document.LoadBObject<PrefabDocument, PrefabAsset>(path);
            Require(prefab.objectCount == 2 && File.ReadAllText(path).Contains("BEngine.Prefab"),
                "Prefab YAML did not preserve the hierarchy.");
            var scene = new Scene("Instances");
            var first = PrefabDocumentOperations.Instantiate(
                Document.LoadBObject<PrefabDocument, PrefabAsset>(path), scene);
            var second = PrefabDocumentOperations.Instantiate(
                Document.LoadBObject<PrefabDocument, PrefabAsset>(path), scene);
            Require(first.Id != second.Id && first.transform.Id != second.transform.Id,
                "Prefab instances reused runtime IDs.");
            Require(first.transform.children.Count == 1 && first.GetComponent<Camera2D>()?.size == (Fix64)7,
                "Prefab hierarchy or components did not round-trip.");
            Require(PrefabUtility.IsPartOfPrefabInstance(first), "Prefab instance linkage is missing.");
            var restored = (Scene)Document.FromBObject<SceneDocument>(scene).ToBObject();
            var restoredRoot = restored.rootGameObjects.First();
            Require(PrefabUtility.IsPartOfPrefabInstance(restoredRoot), "Scene reload lost prefab linkage.");
            PrefabUtility.UnpackPrefabInstance(restoredRoot, PrefabUnpackMode.OutermostRoot);
            Require(!PrefabUtility.IsPartOfAnyPrefab(restoredRoot), "Prefab unpack left source linkage behind.");
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    private static void VerifyEventApi()
    {
        var keyboard = Event.KeyboardEvent("^#A");
        Require(keyboard.type == EventType.KeyDown && keyboard.control && keyboard.shift &&
                keyboard.keyCode == KeyCode.A, "KeyboardEvent did not parse Unity modifier syntax.");
        keyboard.Use();
        Require(keyboard.type == EventType.Used, "Event.Use did not consume the event.");
        var pointer = new Event(EventType.MouseDown) { pointerType = PointerType.Pen, pressure = Fix64.Half };
        Require(pointer.isMouse && pointer.isDirectManipulationDevice,
            "Pointer type classification does not match the Unity Event contract.");
        var queued = new Queue<Event>([new Event(EventType.ScrollWheel)]);
        Event.BindQueue(() => queued.TryDequeue(out var item) ? item : null, () => queued.Count);
        Require(Event.GetEventCount() == 1 && Event.PopEvent().type == EventType.ScrollWheel,
            "Event queue compatibility API did not expose the native event queue.");
        Event.ClearCurrent();
    }

    private static void VerifyGpuCommandsAndTextEditing()
    {
        var commands = new List<GpuCanvasCommand>();
        Dispatch(new Event(EventType.Layout), commands, false);
        var secondBoundary = GUITextMetrics.MeasureRenderedAdvance("ab", GUI.skin.textField.fontSize,
            GUIUtility.fontFamily, Fix64.One);
        var click = new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(4 + 4 + secondBoundary, 12), button = 0, clickCount = 1
        };
        Dispatch(click, commands, false);
        Dispatch(new Event(EventType.KeyDown) { character = 'X' }, commands, false);
        commands.Clear();
        Dispatch(new Event(EventType.Repaint), commands, true);
        Require(_text == "abXcdef", $"Single-click caret insertion produced '{_text}' instead of 'abXcdef'.");
        Require(commands.Any(item => item.Type == GpuCanvasCommandType.Text),
            "IMGUI repaint did not produce a GPU text command.");
        Require(commands.Any(item => item.Type == GpuCanvasCommandType.SolidRect && item.Rect.Width <= 2),
            "Focused IMGUI text field did not emit a GPU caret rectangle.");

        Dispatch(new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(60, 12), button = 0, clickCount = 2
        }, commands, false);
        Dispatch(new Event(EventType.KeyDown) { character = 'Z' }, commands, false);
        Require(_text == "Z", "Double-click did not select all text before replacement.");
    }

    private static void VerifyScaledTextCaretAndHitTesting()
    {
        const string text = "Wi中文 Scale";
        const string prefix = "Wi中";
        const int boundary = 3;
        var field = new Rect(8, 6, 340, 28);
        var style = GUI.skin.textField;
        var previousEditorScale = GUIUtility.pixelsPerPoint;
        var previousDeviceScale = GUIUtility.devicePixelsPerPoint;

        try
        {
            var scaleCases = new[] { .5m, .75m, 1m, 1.25m, 1.5m, 1.8m }
                .Select(value => (Editor: value, Device: 1m))
                .Append((Editor: 1.25m, Device: 1.5m));
            foreach (var scaleCase in scaleCases)
            {
                var editorScale = Fix64.FromDecimal(scaleCase.Editor);
                var deviceScale = Fix64.FromDecimal(scaleCase.Device);
                var renderScale = editorScale * deviceScale;
                GUIUtility.pixelsPerPoint = editorScale;
                GUIUtility.devicePixelsPerPoint = deviceScale;
                GUIUtility.keyboardControl = 0;
                GUIUtility.hotControl = 0;
                var value = text;

                void Draw(Event evt, List<GpuCanvasCommand>? commands = null)
                {
                    GUI.BeginFrame(evt, 760, 120, commands ?? []);
                    try { value = GUI.TextField(field, value, style: style); }
                    finally { GUI.EndFrame(); }
                }

                var boundaryOffset = GUITextMetrics.MeasureRenderedAdvance(prefix, style.fontSize,
                    GUIUtility.fontFamily, renderScale);
                var logicalPointer = new Vector2(field.x + 4 + boundaryOffset, field.y + field.height / 2);
                Draw(new Event(EventType.MouseDown)
                {
                    mousePosition = logicalPointer * editorScale,
                    button = 0,
                    clickCount = 1
                });
                Draw(new Event(EventType.KeyDown) { character = '|' });
                Require(value == text.Insert(boundary, "|"),
                    $"EditorScale {scaleCase.Editor:0.##} at device scale {scaleCase.Device:0.##} " +
                    $"placed the ASCII/CJK click at the wrong text boundary: '{value}'.");

                Draw(new Event(EventType.KeyDown) { keyCode = KeyCode.Backspace });
                var commands = new List<GpuCanvasCommand>();
                Draw(new Event(EventType.Repaint), commands);
                var caretColor = GpuCanvasColor.FromColor(style.focused.textColor);
                var caret = commands.Single(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                                       command.Color == caretColor &&
                                                       Math.Abs(command.Rect.Width - (float)renderScale) < .02f);
                var expectedX = (float)((field.x + 4 + boundaryOffset) * renderScale);
                Require(Math.Abs(caret.Rect.X - expectedX) < .06f,
                    $"EditorScale {scaleCase.Editor:0.##} at device scale {scaleCase.Device:0.##} " +
                    "detached the caret from the rendered CJK boundary: " +
                    $"actual={caret.Rect.X:0.###}, expected={expectedX:0.###}.");
            }
        }
        finally
        {
            GUIUtility.keyboardControl = 0;
            GUIUtility.hotControl = 0;
            GUIUtility.devicePixelsPerPoint = previousDeviceScale;
            GUIUtility.pixelsPerPoint = previousEditorScale;
        }
    }

    private static void VerifyAlignedTextEditingAtScale()
    {
        const string source = "Wi中文 Scale";
        const int boundary = 3;
        var field = new Rect(24, 10, 380, 58);
        var previousEditorScale = GUIUtility.pixelsPerPoint;
        var previousDeviceScale = GUIUtility.devicePixelsPerPoint;
        var controls = new[] { "TextField", "TextArea", "PasswordField" };
        var alignments = new[]
        {
            TextAnchor.MiddleLeft,
            TextAnchor.MiddleCenter,
            TextAnchor.MiddleRight
        };

        try
        {
            GUIUtility.devicePixelsPerPoint = Fix64.One;
            foreach (var scaleValue in new[] { .5m, 1m, 1.8m })
            foreach (var control in controls)
            foreach (var alignment in alignments)
            {
                var editorScale = Fix64.FromDecimal(scaleValue);
                var renderScale = editorScale;
                GUIUtility.pixelsPerPoint = editorScale;
                GUIUtility.keyboardControl = 0;
                GUIUtility.hotControl = 0;
                var style = new GUIStyle(control == "TextArea" ? GUI.skin.textArea : GUI.skin.textField)
                {
                    alignment = alignment
                };
                var value = source;
                var visible = control == "PasswordField" ? new string('#', source.Length) : source;

                void Draw(Event evt, List<GpuCanvasCommand>? commands = null)
                {
                    GUI.BeginFrame(evt, 960, 180, commands ?? []);
                    try
                    {
                        value = control switch
                        {
                            "TextArea" => GUI.TextArea(field, value, style: style),
                            "PasswordField" => GUI.PasswordField(field, value, '#', -1, style),
                            _ => GUI.TextField(field, value, style: style)
                        };
                    }
                    finally { GUI.EndFrame(); }
                }

                var initialCommands = new List<GpuCanvasCommand>();
                Draw(new Event(EventType.Repaint), initialCommands);
                var textCommand = initialCommands.Single(command =>
                    command.Type == GpuCanvasCommandType.Text && command.Content == visible);
                var physicalFontSize = (float)(style.fontSize * renderScale);
                Require(EditorGpuCanvasResourceResolver.Shared.TryMeasureTextAdvance(
                        visible, physicalFontSize, GUIUtility.fontFamily, out var fullAdvance),
                    $"{control} {alignment} could not measure its rendered text.");
                Require(EditorGpuCanvasResourceResolver.Shared.TryMeasureTextAdvance(
                        visible[..boundary], physicalFontSize, GUIUtility.fontFamily, out var prefixAdvance),
                    $"{control} {alignment} could not measure its rendered prefix.");
                Require(EditorGpuCanvasResourceResolver.Shared.TryMeasureTextAdvance(
                        visible[..(boundary + 1)], physicalFontSize, GUIUtility.fontFamily, out var nextAdvance),
                    $"{control} {alignment} could not measure its rendered selection boundary.");

                var contentLeft = (float)((field.x + 4) * renderScale);
                var contentRight = (float)((field.xMax - 4) * renderScale);
                var expectedTextX = alignment switch
                {
                    TextAnchor.MiddleCenter => (contentLeft + contentRight - fullAdvance) / 2,
                    TextAnchor.MiddleRight => contentRight - fullAdvance,
                    _ => contentLeft
                };
                Require(Math.Abs(textCommand.Rect.X - expectedTextX) < .08f,
                    $"{control} {alignment} at EditorScale {scaleValue:0.0} rendered from " +
                    $"{textCommand.Rect.X:0.###}, expected {expectedTextX:0.###}.");

                Draw(new Event(EventType.MouseDown)
                {
                    mousePosition = new Vector2(
                        (Fix64)(textCommand.Rect.X + prefixAdvance),
                        (field.y + field.height / 2) * editorScale),
                    button = 0,
                    clickCount = 1
                });
                Draw(new Event(EventType.KeyDown) { character = '|' });
                Require(value == source.Insert(boundary, "|"),
                    $"{control} {alignment} at EditorScale {scaleValue:0.0} clicked the wrong " +
                    $"ASCII/CJK boundary: '{value}'.");

                Draw(new Event(EventType.KeyDown) { keyCode = KeyCode.Backspace });
                var caretCommands = new List<GpuCanvasCommand>();
                Draw(new Event(EventType.Repaint), caretCommands);
                var focusedText = caretCommands.Single(command =>
                    command.Type == GpuCanvasCommandType.Text && command.Content == visible);
                var caretColor = GpuCanvasColor.FromColor(style.focused.textColor);
                var caret = caretCommands.Single(command =>
                    command.Type == GpuCanvasCommandType.SolidRect &&
                    command.Color == caretColor &&
                    Math.Abs(command.Rect.Width - (float)renderScale) < .02f);
                var expectedCaretX = focusedText.Rect.X + prefixAdvance;
                Require(Math.Abs(caret.Rect.X - expectedCaretX) < .08f,
                    $"{control} {alignment} at EditorScale {scaleValue:0.0} detached its caret " +
                    $"from rendered text: {caret.Rect.X:0.###} vs {expectedCaretX:0.###}.");

                Draw(new Event(EventType.KeyDown)
                {
                    keyCode = KeyCode.RightArrow,
                    modifiers = EventModifiers.Shift
                });
                var selectionCommands = new List<GpuCanvasCommand>();
                Draw(new Event(EventType.Repaint), selectionCommands);
                var selectedText = selectionCommands.First(command =>
                    command.Type == GpuCanvasCommandType.Text && command.Content == visible);
                var selectionColor = GpuCanvasColor.FromColor(
                    EditorStyles.selectionRect.normal.backgroundColor);
                var selection = selectionCommands.Single(command =>
                    command.Type == GpuCanvasCommandType.SolidRect && command.Color == selectionColor);
                Require(Math.Abs(selection.Rect.X - (selectedText.Rect.X + prefixAdvance)) < .08f &&
                        Math.Abs(selection.Rect.Width - (nextAdvance - prefixAdvance)) < .08f,
                    $"{control} {alignment} at EditorScale {scaleValue:0.0} detached its selection " +
                    "from the rendered glyph boundaries.");
            }
        }
        finally
        {
            GUIUtility.keyboardControl = 0;
            GUIUtility.hotControl = 0;
            GUIUtility.devicePixelsPerPoint = previousDeviceScale;
            GUIUtility.pixelsPerPoint = previousEditorScale;
        }
    }

    private static void VerifyNumericEditingBuffers()
    {
        var floatValue = 1f;
        void DrawFloat(Event evt)
        {
            GUI.BeginFrame(evt, 360, 80, []);
            try { floatValue = EditorGUI.FloatField(new Rect(4, 4, 300, 24), floatValue); }
            finally { GUI.EndFrame(); }
        }

        GUI.FocusControl(string.Empty);
        DrawFloat(new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(20, 12), button = 0, clickCount = 1
        });
        DrawFloat(new Event(EventType.KeyDown) { character = '.' });
        DrawFloat(new Event(EventType.KeyDown) { character = '5' });
        Require(Math.Abs(floatValue - 1.5f) < 0.0001f,
            $"FloatField discarded its intermediate decimal buffer and produced {floatValue}.");

        DrawFloat(new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(20, 12), button = 0, clickCount = 2
        });
        DrawFloat(new Event(EventType.KeyDown) { keyCode = KeyCode.Backspace });
        DrawFloat(new Event(EventType.KeyDown) { character = '2' });
        Require(Math.Abs(floatValue - 2f) < 0.0001f,
            $"FloatField restored its old value after clearing the edit buffer: {floatValue}.");

        var integerValue = 23;
        void DrawInteger(Event evt)
        {
            GUI.BeginFrame(evt, 360, 80, []);
            try { integerValue = EditorGUI.IntField(new Rect(4, 4, 300, 24), integerValue); }
            finally { GUI.EndFrame(); }
        }

        GUI.FocusControl(string.Empty);
        DrawInteger(new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(20, 12), button = 0, clickCount = 2
        });
        DrawInteger(new Event(EventType.KeyDown) { character = '-' });
        DrawInteger(new Event(EventType.KeyDown) { character = '7' });
        Require(integerValue == -7,
            $"IntField discarded its intermediate sign buffer and produced {integerValue}.");
        GUI.FocusControl(string.Empty);
    }

    private static void VerifyFieldFocusIsolation()
    {
        var first = new Rect(8, 8, 180, 22);
        var second = new Rect(8, 36, 180, 22);
        var style = new GUIStyle(EditorStyles.textField) { borderWidth = 0 };
        style.normal.backgroundColor = new Color(Fix64.FromDecimal(.08m),
            Fix64.FromDecimal(.09m), Fix64.FromDecimal(.1m), 1);
        style.focused.backgroundColor = new Color(Fix64.FromDecimal(.08m),
            Fix64.FromDecimal(.36m), Fix64.FromDecimal(.72m), 1);

        void DrawFields()
        {
            Require(EditorFeatureGuard.Invoke("Focus isolation first",
                    () => _ = GUI.TextField(first, "First", style: style)),
                "The first isolated field failed to draw.");
            Require(EditorFeatureGuard.Invoke("Focus isolation second",
                    () => _ = GUI.TextField(second, "Second", style: style)),
                "The second isolated field failed to draw.");
        }

        GUI.FocusControl(string.Empty);
        GUI.BeginFrame(new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(40, 46),
            button = 0,
            clickCount = 1
        }, 240, 80, []);
        try { DrawFields(); }
        finally { GUI.EndFrame(); }

        var commands = new List<GpuCanvasCommand>();
        GUI.BeginFrame(new Event(EventType.Repaint)
        {
            mousePosition = new Vector2(220, 70)
        }, 240, 80, commands);
        try { DrawFields(); }
        finally { GUI.EndFrame(); }

        var focusedColor = GpuCanvasColor.FromColor(style.focused.backgroundColor);
        var normalColor = GpuCanvasColor.FromColor(style.normal.backgroundColor);
        var fieldSurfaces = commands.Where(command =>
            command.Type == GpuCanvasCommandType.SolidRect &&
            (command.Color == focusedColor || command.Color == normalColor)).ToArray();
        Require(fieldSurfaces.Count(command => command.Color == focusedColor) == 1 &&
                fieldSurfaces.Single(command => command.Color == focusedColor).Rect.Y >= 36 &&
                fieldSurfaces.Count(command => command.Color == normalColor) == 1 &&
                fieldSurfaces.Single(command => command.Color == normalColor).Rect.Y < 30,
            "Keyboard focus styling leaked from the clicked field to another isolated field.");
        GUI.FocusControl(string.Empty);
    }

    private static void VerifyWindowCoordinatesAndScrolling()
    {
        var commands = new List<GpuCanvasCommand>();
        var down = new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(125, 75), button = 0
        };
        GUI.BeginFrame(down, 500, 300, []);
        try
        {
            using (GUI.BeginWindow(new Rect(100, 50, 200, 120)))
            {
                Require(Event.current.mousePosition == new Vector2(25, 25),
                    "Docked window did not receive local mouse coordinates.");
                GUI.Button(new Rect(10, 10, 80, 30), "Local button");
            }
            Require(Event.current.mousePosition == new Vector2(125, 75),
                "Window scope did not restore root mouse coordinates.");
        }
        finally { GUI.EndFrame(); }

        var up = new Event(EventType.MouseUp)
        {
            mousePosition = new Vector2(125, 75), button = 0
        };
        var clicked = false;
        GUI.BeginFrame(up, 500, 300, []);
        try
        {
            using (GUI.BeginWindow(new Rect(100, 50, 200, 120)))
                clicked = GUI.Button(new Rect(10, 10, 80, 30), "Local button");
        }
        finally { GUI.EndFrame(); }
        Require(clicked, "A dock-local button did not retain its hot control across events.");

        var wheel = new Event(EventType.ScrollWheel)
        {
            mousePosition = new Vector2(20, 20), delta = new Vector2(0, 1)
        };
        Vector2 scroll;
        GUI.BeginFrame(wheel, 300, 200, commands);
        try
        {
            scroll = GUI.BeginScrollView(new Rect(10, 10, 120, 60), Vector2.zero,
                new Rect(0, 0, 109, 300));
            GUI.EndScrollView();
        }
        finally { GUI.EndFrame(); }
        Require(scroll.y == (Fix64)24 && wheel.type == EventType.Used,
            "GPU scroll view did not consume and apply the wheel event.");

        scroll = Vector2.zero;
        void DrawWindowScroll(Event evt)
        {
            GUI.BeginFrame(evt, 500, 300, []);
            try
            {
                using (GUI.BeginWindow(new Rect(100, 50, 200, 120)))
                {
                    scroll = GUI.BeginScrollView(new Rect(10, 10, 120, 60), scroll,
                        new Rect(0, 0, 109, 300));
                    GUI.EndScrollView();
                }
            }
            finally { GUI.EndFrame(); }
        }

        var thumbDown = new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(225, 72), button = 0
        };
        DrawWindowScroll(thumbDown);
        Require(GUIUtility.hotControl != 0 && thumbDown.type == EventType.Used,
            "Vertical scrollbar thumb did not capture the mouse on press.");
        var thumbDrag = new Event(EventType.MouseDrag)
        {
            mousePosition = new Vector2(225, 102), delta = new Vector2(0, 30), button = 0
        };
        DrawWindowScroll(thumbDrag);
        Require(Fix64.Abs(scroll.y - 200) <= Fix64.Parse("0.001") && thumbDrag.type == EventType.Used,
            $"Vertical scrollbar drag produced {scroll.y} instead of 200 in window-local coordinates.");
        var thumbUp = new Event(EventType.MouseUp)
        {
            mousePosition = new Vector2(225, 102), button = 0
        };
        DrawWindowScroll(thumbUp);
        Require(GUIUtility.hotControl == 0 && thumbUp.type == EventType.Used,
            "Vertical scrollbar thumb did not release its mouse capture.");

        scroll = Vector2.zero;
        DrawWindowScroll(new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(225, 72), button = 0
        });
        Require(GUIUtility.hotControl != 0,
            "Vertical scrollbar did not capture before the disappearing-range test.");
        var rangeDisappearedUp = new Event(EventType.MouseUp)
        {
            mousePosition = new Vector2(225, 72), button = 0
        };
        GUI.BeginFrame(rangeDisappearedUp, 500, 300, []);
        try
        {
            using (GUI.BeginWindow(new Rect(100, 50, 200, 120)))
            {
                scroll = GUI.BeginScrollView(new Rect(10, 10, 120, 60), scroll,
                    new Rect(0, 0, 109, 49));
                GUI.EndScrollView();
            }
        }
        finally { GUI.EndFrame(); }
        Require(GUIUtility.hotControl == 0 && rangeDisappearedUp.type == EventType.Used,
            "A scrollbar whose range disappeared did not release hotControl on MouseUp.");

        scroll = Vector2.zero;
        DrawWindowScroll(new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(225, 72), button = 0
        });
        Require(GUIUtility.hotControl != 0,
            "Vertical scrollbar did not capture before the disabled-control test.");
        var disabledUp = new Event(EventType.MouseUp)
        {
            mousePosition = new Vector2(225, 72), button = 0
        };
        GUI.enabled = false;
        try { DrawWindowScroll(disabledUp); }
        finally { GUI.enabled = true; }
        Require(GUIUtility.hotControl == 0 && disabledUp.type == EventType.Used,
            "A disabled scrollbar did not release hotControl on MouseUp.");

        scroll = Vector2.zero;
        void DrawHorizontalScroll(Event evt)
        {
            GUI.BeginFrame(evt, 500, 300, []);
            try
            {
                using (GUI.BeginWindow(new Rect(100, 50, 200, 120)))
                {
                    scroll = GUI.BeginScrollView(new Rect(10, 10, 120, 60), scroll,
                        new Rect(0, 0, 300, 49));
                    GUI.EndScrollView();
                }
            }
            finally { GUI.EndFrame(); }
        }

        DrawHorizontalScroll(new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(130, 115), button = 0
        });
        DrawHorizontalScroll(new Event(EventType.MouseDrag)
        {
            mousePosition = new Vector2(166, 115), delta = new Vector2(36, 0), button = 0
        });
        Require(Fix64.Abs(scroll.x - 90) <= Fix64.Parse("0.001"),
            $"Horizontal scrollbar drag produced {scroll.x} instead of 90.");
        DrawHorizontalScroll(new Event(EventType.MouseUp)
        {
            mousePosition = new Vector2(166, 115), button = 0
        });
        Require(GUIUtility.hotControl == 0,
            "Horizontal scrollbar thumb did not release its mouse capture.");
    }

    private static void VerifyNativeMouseMoveClassification()
    {
        GUIUtility.hotControl = 0;
        Require(ImGuiNativeWindow.ResolveMouseMoveEventType(anyMouseButtonPressed: true) ==
                EventType.MouseDrag,
            "A move queued immediately after MouseDown was not classified as MouseDrag.");
        GUIUtility.hotControl = 123;
        Require(ImGuiNativeWindow.ResolveMouseMoveEventType(anyMouseButtonPressed: false) ==
                    EventType.MouseMove && GUIUtility.hotControl == 0,
            "Native mouse move routing did not clear stale capture after button release.");
    }

    private static void VerifyEditorWindowRoutingAndDockTabs()
    {
        var first = new ProbeWindow("Scene");
        var second = new ProbeWindow("Game");
        var left = new ProbeWindow("Hierarchy");
        var right = new ProbeWindow("Inspector");
        var bottom = new ProbeWindow("Project");
        foreach (var window in new[] { first, second, left, right, bottom }) window.OpenInternal();
        first.FocusInternal();
        EditorWindow.SetMouseOverWindow(first);
        Require(first.hasFocus && ReferenceEquals(EditorWindow.focusedWindow, first) &&
                ReferenceEquals(EditorWindow.mouseOverWindow, first),
            "EditorWindow focus or mouse-over compatibility state was not set.");
        GUIUtility.hotControl = 71;
        GUIUtility.keyboardControl = 72;
        GUIUtility.textFieldInput = true;
        second.FocusInternal();
        Require(!first.hasFocus && second.hasFocus && first.LostFocusCount == 1 && second.FocusCount == 1,
            "EditorWindow focus lifecycle leaked between windows.");
        Require(GUIUtility.hotControl == 0 && GUIUtility.keyboardControl == 0 &&
                !GUIUtility.textFieldInput,
            "An unfocused EditorWindow retained mouse capture or text keyboard ownership.");

        var dock = new ImGuiDockWorkspace();
        dock.Add("Hierarchy", left, DockArea.Left, true);
        dock.Add("Scene", first, DockArea.Center, true);
        dock.Add("Game", second, DockArea.Center, false);
        dock.Add("Inspector", right, DockArea.Right, true);
        dock.Add("Project", bottom, DockArea.Bottom, true);
        RenderDock(dock, new Event(EventType.Layout), []);
        first.FocusInternal();
        var tabMouseDown = new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(295, 10), button = 0
        };
        RenderDock(dock, tabMouseDown, []);
        Require(dock.IsSelected(second) && second.hasFocus && tabMouseDown.type == EventType.Used,
            "A dock tab did not select, focus, and consume the first mouse-down event.");
        RenderDock(dock, new Event(EventType.MouseUp)
        {
            mousePosition = new Vector2(295, 10), button = 0
        }, []);
        Require(dock.IsSelected(second), "Scene/Game dock tabs could not switch the selected window.");

        var originalToolbarSize = EditorStyles.toolbarButton.fontSize;
        try
        {
            EditorStyles.toolbarButton.fontSize = 20;
            var resolverType = typeof(GUI).Assembly.GetType("BEngine.Editor.EditorGpuCanvasResourceResolver") ??
                               throw new InvalidOperationException("GPU text resolver was not found.");
            var resolver = resolverType.GetProperty("Shared", BindingFlags.Static | BindingFlags.Public)!
                .GetValue(null)!;
            var measure = resolverType.GetMethod("TryMeasureText", BindingFlags.Instance | BindingFlags.Public)!;
            object?[] arguments = ["Game", 20f, GUIUtility.fontFamily, 0];
            Require((bool)measure.Invoke(resolver, arguments)! && arguments[3] is int,
                "GPU text resolver could not measure the Game tab title.");
            var measuredWidth = (int)arguments[3]!;
            var layoutWidth = EditorStyles.toolbarButton.CalcSize(new GUIContent("Game")).x;
            Require(layoutWidth >= (Fix64)measuredWidth,
                "Dock title layout is narrower than the rendered Game title.");
        }
        finally
        {
            EditorStyles.toolbarButton.fontSize = originalToolbarSize;
        }

        var commands = new List<GpuCanvasCommand>();
        RenderDock(dock, new Event(EventType.Repaint) { mousePosition = new Vector2(300, 100) }, commands);
        var borderCommands = commands.Count(command => command.Type == GpuCanvasCommandType.SolidRect &&
            (command.Rect.Width <= 2.1f || command.Rect.Height <= 2.1f));
        Require(borderCommands >= 4, "Dock windows did not emit their 2px outer border.");

        var originalDockHeight = EditorStyles.dockTab.fixedHeight;
        var originalTitleHeight = EditorStyles.windowTitle.fixedHeight;
        var originalIconHeight = EditorStyles.toolbarIconButton.fixedHeight;
        try
        {
            EditorStyles.dockTab.fixedHeight = 31;
            EditorStyles.windowTitle.fixedHeight = 33;
            EditorStyles.toolbarIconButton.fixedHeight = 29;
            commands.Clear();
            RenderDock(dock, new Event(EventType.Repaint) { mousePosition = new Vector2(300, 100) }, commands);
            Require(second.position.y == (Fix64)35,
                "Dock content did not move below the dynamically-sized title chrome.");
            var gameTitle = commands.Where(command => command.Type == GpuCanvasCommandType.Text &&
                                                        command.Content == "Game")
                .OrderBy(command => command.Rect.Y)
                .First();
            var windowOptions = commands.Where(command => command.Type == GpuCanvasCommandType.Image &&
                                                           command.Content.EndsWith("More.png",
                                                               StringComparison.Ordinal) &&
                                                           command.Rect.X > gameTitle.Rect.X)
                .OrderBy(command => command.Rect.X)
                .First();
            Require(gameTitle.Rect.X + gameTitle.Rect.Width <= windowOptions.Rect.X,
                "Dock title text overlaps the fixed window action area.");
        }
        finally
        {
            EditorStyles.dockTab.fixedHeight = originalDockHeight;
            EditorStyles.windowTitle.fixedHeight = originalTitleHeight;
            EditorStyles.toolbarIconButton.fixedHeight = originalIconHeight;
        }
        foreach (var window in new[] { first, second, left, right, bottom }) window.CloseInternal();
    }

    private static void VerifyProjectTreeVisualLayout()
    {
        var applicationType = typeof(EditorWindow).Assembly.GetType(
            "BEngine.Editor.GpuEditorApplication", throwOnError: true)!;
        var projectWindowType = applicationType.GetNestedType(
            "ImGuiProjectWindow", BindingFlags.NonPublic)!;
        var projectWindow = (EditorWindow)(Activator.CreateInstance(projectWindowType, nonPublic: true) ??
            throw new InvalidOperationException("Project window could not be created for visual validation."));
        var items = new[]
        {
            new ProjectBrowserItem("Assets", "Assets", "C:/Project/Assets", "Folder", true, false),
            new ProjectBrowserItem("Packages", "Packages", "C:/Project/Packages", "Folder", true, true)
        };
        projectWindowType.GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(projectWindow, items);
        projectWindowType.GetField("_selectedPath", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(projectWindow, "Assets");
        projectWindowType.GetField("_projectBrowserMode", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(projectWindow, "OneColumn");

        var previousSkin = GUI.skin;
        var previousTreeFontSize = EditorStyles.treeViewRow.fontSize;
        var previousSelectedTreeFontSize = EditorStyles.treeViewRowSelected.fontSize;
        var skin = new GUISkin();
        skin.button.fontSize = 20;
        GUI.skin = skin;
        EditorStyles.treeViewRow.fontSize = 20;
        EditorStyles.treeViewRowSelected.fontSize = 20;
        var commands = new List<GpuCanvasCommand>();
        try
        {
            GUI.BeginFrame(new Event(EventType.Repaint), 640, 260, commands);
            try { projectWindow.OnGUIInternal(); }
            finally { GUI.EndFrame(); }
        }
        finally
        {
            GUI.skin = previousSkin;
            EditorStyles.treeViewRow.fontSize = previousTreeFontSize;
            EditorStyles.treeViewRowSelected.fontSize = previousSelectedTreeFontSize;
        }

        var assets = commands.Where(command => command.Type == GpuCanvasCommandType.Text &&
                                                command.Content == "Assets")
            .OrderBy(command => command.Rect.Y)
            .First();
        var packages = commands.Where(command => command.Type == GpuCanvasCommandType.Text &&
                                                  command.Content == "Packages")
            .OrderBy(command => command.Rect.Y)
            .First();
        Require(TextFitsClip(assets) && TextFitsClip(packages),
            "A Project TreeView row clips the bottom of its text command.");
        Require(assets.Rect.Height >= 28 && packages.Rect.Height >= 28,
            "A Project TreeView row is shorter than the configured 20px font line height.");
        var selectionColor = GpuCanvasColor.FromColor(EditorStyles.treeViewRowSelected.normal.backgroundColor);
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                        command.Color == selectionColor &&
                                        command.Rect.Y <= assets.Rect.Y &&
                                        command.Rect.Bottom >= assets.Rect.Bottom),
            "The selected Project TreeView row does not use the shared Unity-style selection state.");

        var boundaryStart = assets.Rect.Bottom - 0.5f;
        var boundaryEnd = packages.Rect.Y + 2.5f;
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                        command.Rect.Width >= 500 &&
                                        command.Rect.Height is >= 1.9f and <= 2.1f &&
                                        command.Rect.Y >= boundaryStart &&
                                        command.Rect.Bottom <= boundaryEnd),
            "The Project TreeView has no visible separator between Assets and Packages.");

        projectWindowType.GetField("_search", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(projectWindow, "Packages");
        commands.Clear();
        GUI.BeginFrame(new Event(EventType.Repaint), 640, 260, commands);
        try { projectWindow.OnGUIInternal(); }
        finally { GUI.EndFrame(); }
        var filteredPackages = commands.Where(command => command.Type == GpuCanvasCommandType.Text &&
                                                         command.Content == "Packages")
            .OrderByDescending(command => command.Rect.Y)
            .First();
        Require(!commands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                         command.Rect.Width >= 500 &&
                                         command.Rect.Height is >= 1.9f and <= 2.1f &&
                                         command.Rect.Y >= 20 &&
                                         command.Rect.Bottom <= filteredPackages.Rect.Y + 0.1f),
            "A separator was drawn without a visible Assets section above Packages.");
    }

    private static bool TextFitsClip(GpuCanvasCommand command) =>
        command.Rect.Y >= command.ClipRect.Y - 0.01f &&
        command.Rect.Bottom <= command.ClipRect.Bottom + 0.01f;

    private static void RenderDock(ImGuiDockWorkspace dock, Event evt, List<GpuCanvasCommand> commands)
    {
        GUI.BeginFrame(evt, 1000, 700, commands);
        try { dock.OnGUI(new Rect(0, 0, 1000, 700)); }
        finally { GUI.EndFrame(); }
    }

    private static void Dispatch(Event evt, List<GpuCanvasCommand> commands, bool collect)
    {
        GUI.BeginFrame(evt, 360, 80, collect ? commands : []);
        try
        {
            Require(ReferenceEquals(Event.current, evt), "Event.current is not the active OnGUI event.");
            _text = GUI.TextField(new Rect(4, 4, 300, 24), _text);
        }
        finally { GUI.EndFrame(); }
    }

    private static void VerifyAssemblyBoundary()
    {
        var runtime = typeof(BObject).Assembly;
        var editor = typeof(EditorWindow).Assembly;
        var references = editor.GetReferencedAssemblies().Select(item => item.Name).ToArray();
        Require(!references.Contains("BEngine.UIElements", StringComparer.OrdinalIgnoreCase),
            "BEngine.Editor still references the optional UIElements package.");
        Require(editor.GetType("BEngine.Editor.ImGuiNativeWindow", false) is not null,
            "BEngine.Editor is missing the native GPU IMGUI window.");
        Require(ReferenceEquals(typeof(Event).Assembly, editor),
            "Event and the IMGUI API must live in BEngine.Editor.");
        foreach (var runtimeType in new[] { "GUI", "GUILayout", "GUIContent", "GUIStyle", "GUISkin", "GUIUtility", "Event", "EventType" })
            Require(runtime.GetType($"BEngine.{runtimeType}", false) is null,
                $"BEngine runtime still exports the editor-only IMGUI type BEngine.{runtimeType}.");
        foreach (var editorType in new[] { "GUI", "GUILayout", "GUIContent", "GUIStyle", "GUISkin", "GUIUtility", "Event", "EventType" })
            Require(editor.GetType($"BEngine.Editor.{editorType}", false) is not null,
                $"BEngine.Editor is missing IMGUI type BEngine.Editor.{editorType}.");
        Require(runtime.GetType("BEngine.Rendering.GpuCanvasRenderer", false) is null,
            "BEngine runtime still exports the editor-only GPU canvas renderer.");
        Require(editor.GetType("BEngine.Editor.Rendering.GpuCanvasRenderer", false) is not null,
            "BEngine.Editor is missing the GPU canvas renderer.");
        Require(runtime.GetType("BEngine.Editor.Documents.AssemblyDefinitionDocument", false) is null,
            "BEngine runtime still exports the editor-only assembly definition document.");
        Require(editor.GetType("BEngine.Editor.Documents.AssemblyDefinitionDocument", false) is not null,
            "BEngine.Editor is missing the assembly definition document.");
        Require(runtime.GetType("BEngine.UIElements.VisualElement", false) is null,
            "BEngine core still contains optional UIElements types.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ProbeWindow : EditorWindow
    {
        public ProbeWindow(string title) => titleContent = new GUIContent(title);
        public int FocusCount { get; private set; }
        public int LostFocusCount { get; private set; }
        protected override void OnFocus() => FocusCount++;
        protected override void OnLostFocus() => LostFocusCount++;
        protected override void OnGUI() => GUILayout.Label(titleContent);
    }
}
