using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using BEngine;
using BEngine.Editor;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.InspectorFieldRendering;

internal static class Program
{
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod("BeginFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod("EndFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static object? _capturedMenuItems;
    private static bool _capturedMenuAdvanced;

    private static int Main()
    {
        try
        {
            VerifyNarrowVectorAndPrecision();
            VerifyVector4ResponsiveLayout();
            VerifyNarrowSerializedComponentFields();
            VerifyClippedInspectorVectorLayout();
            VerifyHighDpiVectorLayout();
            VerifyColorSwatchAndAlphaPicker();
            VerifyEnumDropdown();
            VerifyObjectFields();
            VerifyRangeSlider();
            Console.WriteLine(
                "INSPECTOR_FIELD_RENDERING_OK|vector24-responsive,vector4dp,color-alpha,enum-dropdown," +
                "advanced-popup,object-field,object-dragdrop,range-slider");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifyVector4ResponsiveLayout()
    {
        var vector2 = Render(200, 40, () => EditorGUI.Vector2Field(new Rect(0, 0, 200, 18), "Size",
            new Vector2(Fix64.FromDecimal(1.234567m), Fix64.FromDecimal(-2.5m))));
        VerifyAxisFieldOrdering(vector2.Where(command => command.Type == GpuCanvasCommandType.Text).ToArray(),
            ["X", "Y"], ["1.2346", "-2.5"]);

        var inline = Render(360, 40, () => EditorGUI.Vector4Field(new Rect(0, 0, 360, 18), "Weights",
            new Vector4(Fix64.FromDecimal(1.234567m), Fix64.FromDecimal(-2.5m),
                Fix64.FromDecimal(3.75m), Fix64.FromDecimal(4.125m))));
        var texts = inline.Where(command => command.Type == GpuCanvasCommandType.Text).ToArray();
        VerifyAxisFieldOrdering(texts, ["X", "Y", "Z", "W"], ["1.2346", "-2.5", "3.75", "4.125"]);

        var probe = ScriptableObject.CreateInstance<InspectorProbe>();
        using var serialized = new SerializedObject(probe);
        var property = serialized.FindProperty(nameof(InspectorProbe.weights))!;
        var narrow = Render(300, 160, () => EditorGUILayout.PropertyField(property));
        var axes = new[] { "X", "Y", "Z", "W" }.Select(axis => narrow.Single(command =>
            command.Type == GpuCanvasCommandType.Text && command.Content == axis)).ToArray();
        Require(axes.Zip(axes.Skip(1)).All(pair => pair.First.Rect.Y < pair.Second.Rect.Y),
            "A narrow Inspector did not stack all Vector4 components vertically.");
        Require(narrow.All(command => command.Rect.Right <= 300.1f),
            "A narrow Vector4 field escaped the Inspector bounds.");
    }

    private static void VerifyNarrowVectorAndPrecision()
    {
        var commands = Render(280, 40, () => EditorGUI.Vector2Field(new Rect(0, 0, 280, 18), "Position",
            new Vector2(Fix64.FromDecimal(1.234567m), Fix64.FromDecimal(-35.123456m))));
        var texts = commands.Where(command => command.Type == GpuCanvasCommandType.Text).ToArray();
        var renderedText = string.Join("|", texts.Select(command => command.Content));
        Require(texts.Any(command => command.Content == "1.2346"),
            $"Vector X was not rounded to four decimals: {renderedText}");
        Require(texts.Any(command => command.Content == "-35.1235"),
            $"Vector Y was not rounded to four decimals: {renderedText}");
        VerifyAxisFieldOrdering(texts, ["X", "Y"], ["1.2346", "-35.1235"]);
        foreach (var value in texts.Where(command => command.Content is "1.2346" or "-35.1235"))
        {
            Require(value.Rect.Width > 0, $"Vector value '{value.Content}' has no visible field width.");
            Require(value.Rect.X >= 0 && value.Rect.Right <= 280.1f,
                $"Vector value '{value.Content}' escaped the narrow Inspector row.");
        }

        var probe = ScriptableObject.CreateInstance<InspectorProbe>();
        using var serialized = new SerializedObject(probe);
        var property = serialized.FindProperty(nameof(InspectorProbe.offset))!;
        var stacked = Render(260, 60, () => EditorGUILayout.PropertyField(property));
        var compactTexts = stacked.Where(command => command.Type == GpuCanvasCommandType.Text).ToArray();
        var label = compactTexts.Single(command => command.Content == "Offset");
        var axes = compactTexts.Where(command => command.Content is "X" or "Y").ToArray();
        Require(axes.Length == 2, "A compact Inspector did not render both Vector2 axes.");
        var axesBelowLabel = axes.All(command => command.Rect.Y > label.Rect.Y);
        var axesAfterLabel = axes.All(command => Math.Abs(command.Rect.Y - label.Rect.Y) < 0.01f) &&
                             label.Rect.Right <= axes[0].Rect.X;
        Require(axesBelowLabel || axesAfterLabel,
            "A compact Inspector overlapped its Vector2 label and axes.");
        VerifyAxisFieldOrdering(compactTexts, ["X", "Y"], ["11.25", "-22.5"]);
    }

    private static void VerifyNarrowSerializedComponentFields()
    {
        var probe = ScriptableObject.CreateInstance<InspectorProbe>();
        using var serialized = new SerializedObject(probe);

        VerifyNarrowSerializedComponentField(serialized, nameof(InspectorProbe.offset), 144,
            ["X", "Y"], ["11.25", "-22.5"]);
        VerifyNarrowSerializedComponentField(serialized, nameof(InspectorProbe.weights), 192,
            ["X", "Y", "Z", "W"], ["1.2346", "-2.5", "3.75", "4.125"]);
    }

    private static void VerifyNarrowSerializedComponentField(SerializedObject serialized, string propertyName,
        int inspectorWidth, IReadOnlyList<string> axes, IReadOnlyList<string> values)
    {
        var property = serialized.FindProperty(propertyName)!;
        var commands = Render(inspectorWidth, 220, () => EditorGUILayout.PropertyField(property));
        var texts = commands.Where(command => command.Type == GpuCanvasCommandType.Text).ToArray();
        var axisCommands = axes.Select(axis => texts.Single(command => command.Content == axis)).ToArray();
        var valueCommands = values.Select(value => texts.Single(command => command.Content == value)).ToArray();

        Require(axisCommands.Zip(axisCommands.Skip(1)).All(pair => pair.First.Rect.Y < pair.Second.Rect.Y),
            $"Narrow Inspector property '{propertyName}' did not stack every component on its own row.");
        for (var index = 0; index < axes.Count; index++)
        {
            var axis = axisCommands[index];
            var field = valueCommands[index];
            var requiredAxisWidth = (float)EditorStyles.vectorAxisLabel.CalcSize(new GUIContent(axes[index])).x;
            Require(axis.Rect.Width + 0.01f >= requiredAxisWidth,
                $"Property '{propertyName}' clipped the complete {axes[index]} axis label: " +
                $"{axis.Rect.Width} < {requiredAxisWidth}.");
            Require(axis.Rect.Right <= field.Rect.X,
                $"Property '{propertyName}' axis {axes[index]} overlaps its numeric field.");
            Require(field.Rect.Width >= 40,
                $"Property '{propertyName}' axis {axes[index]} has no readable numeric field width: " +
                $"{field.Rect.Width}.");
            Require(field.Rect.Right <= inspectorWidth + 0.1f,
                $"Property '{propertyName}' axis {axes[index]} escaped the Inspector width.");
            Require(!axisCommands.Where((_, other) => other != index)
                    .Any(other => RectsOverlap(field.Rect, other.Rect)),
                $"Property '{propertyName}' axis {axes[index]} field overlaps another axis label.");
            Require(!valueCommands.Where((_, other) => other != index)
                    .Any(other => RectsOverlap(field.Rect, other.Rect)),
                $"Property '{propertyName}' axis {axes[index]} numeric fields overlap.");
        }

    }

    private static void VerifyClippedInspectorVectorLayout()
    {
        var probe = ScriptableObject.CreateInstance<InspectorProbe>();
        using var serialized = new SerializedObject(probe);
        var property = serialized.FindProperty(nameof(InspectorProbe.offset))!;
        var commands = Render(420, 120, () =>
        {
            GUI.BeginClip(new Rect(0, 0, 190, 120));
            try { EditorGUILayout.PropertyField(property); }
            finally { GUI.EndClip(); }
        });
        var texts = commands.Where(command => command.Type == GpuCanvasCommandType.Text).ToArray();
        var axes = new[] { "X", "Y" }.Select(axis =>
            texts.Single(command => command.Content == axis)).ToArray();
        var fields = new[] { "11.25", "-22.5" }.Select(value =>
            texts.Single(command => command.Content == value)).ToArray();
        for (var index = 0; index < axes.Length; index++)
        {
            var requiredAxisWidth = (float)EditorStyles.vectorAxisLabel
                .CalcSize(new GUIContent(axes[index].Content)).x;
            Require(axes[index].Rect.Width + 0.01f >= requiredAxisWidth,
                $"Axis {axes[index].Content} label is too narrow to render the complete glyph: " +
                $"{axes[index].Rect.Width} < {requiredAxisWidth}.");
            Require(axes[index].Rect.Right <= fields[index].Rect.X,
                $"Clipped axis {axes[index].Content} overlaps its numeric field.");
            Require(fields[index].Rect.Width >= 58,
                $"Clipped axis {axes[index].Content} did not receive a readable numeric field.");
            Require(fields[index].Rect.Right <= 190.1f,
                $"Clipped axis {axes[index].Content} escaped the visible Inspector width.");
        }
        Require(fields[0].Rect.Right <= axes[1].Rect.X || fields[0].Rect.Y < axes[1].Rect.Y,
            "Clipped Vector2 fields overlap each other.");
    }

    private static void VerifyHighDpiVectorLayout()
    {
        var property = typeof(GUIUtility).GetField("devicePixelsPerPoint",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var probe = ScriptableObject.CreateInstance<InspectorProbe>();
        using var serialized = new SerializedObject(probe);
        var vector = serialized.FindProperty(nameof(InspectorProbe.offset))!;
        var baseline = Render(210, 120, () => EditorGUILayout.PropertyField(vector));
        property.SetValue(null, (Fix64)2);
        try
        {
            var commands = Render(420, 240, () => EditorGUILayout.PropertyField(vector));
            foreach (var content in new[] { "Offset", "X", "Y", "11.25", "-22.5" })
            {
                var logical = baseline.Single(command => command.Type == GpuCanvasCommandType.Text &&
                                                       command.Content == content);
                var scaled = commands.Single(command => command.Type == GpuCanvasCommandType.Text &&
                                                      command.Content == content);
                Require(Math.Abs(scaled.Rect.X / 2 - logical.Rect.X) < 0.1f &&
                        Math.Abs(scaled.Rect.Y / 2 - logical.Rect.Y) < 0.1f &&
                        Math.Abs(scaled.Rect.Width / 2 - logical.Rect.Width) < 0.1f,
                    $"High-DPI layout changed the logical position of '{content}'.");
            }
            Require(commands.All(command => command.Rect.Right <= 420.1f),
                "High-DPI Inspector commands escaped the framebuffer bounds.");
        }
        finally { property.SetValue(null, Fix64.One); }
    }

    private static void VerifyColorSwatchAndAlphaPicker()
    {
        var value = new Color(Fix64.FromDecimal(0.125m), Fix64.FromDecimal(0.25m),
            Fix64.FromDecimal(0.5m), Fix64.FromDecimal(0.375m));
        var commands = Render(320, 40, () => EditorGUI.ColorField(new Rect(0, 0, 320, 18), "Tint", value));
        var texts = commands.Where(command => command.Type == GpuCanvasCommandType.Text)
            .Select(command => command.Content).ToArray();
        Require(texts.SequenceEqual(["Tint"]),
            $"ColorField rendered numeric channels instead of a swatch: {string.Join(",", texts)}");
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                        command.Color == GpuCanvasColor.FromColor(
                                            new Color(value.r, value.g, value.b, 1))),
            "ColorField did not render its RGB color directly.");

        Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(210, 9), button = 0 }, 320, 40,
            () => EditorGUI.ColorField(new Rect(0, 0, 320, 18), "Tint", value));
        Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(210, 9), button = 0 }, 320, 40,
            () => EditorGUI.ColorField(new Rect(0, 0, 320, 18), "Tint", value));
        var picker = EditorWindow.focusedWindow;
        Require(picker?.GetType().Name == "EditorColorPickerWindow", "Clicking the swatch did not open the GPU picker.");
        var pickerCommands = Render(360, 480, () => picker!.GetType()
            .GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, null));
        Require(pickerCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                              command.Content is "Alpha" or "A"),
            "The color picker does not expose an Alpha control.");
        Require(pickerCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                              command.Content.TrimStart('#').Length == 6 &&
                                              command.Content.TrimStart('#').All(Uri.IsHexDigit)),
            "The color picker does not display an RGB hexadecimal value.");
        picker!.Close();
    }

    private static void VerifyEnumDropdown()
    {
        var dispatcher = typeof(EditorWindow).Assembly.GetType("BEngine.Editor.GenericMenuDispatcher", true)!;
        var handler = dispatcher.GetProperty("Handler", BindingFlags.Static | BindingFlags.NonPublic)!;
        var invoke = handler.PropertyType.GetMethod("Invoke")!;
        var parameterType = invoke.GetParameters()[0].ParameterType;
        var parameter = Expression.Parameter(parameterType, "items");
        var capture = typeof(Program).GetMethod(nameof(CaptureMenu), BindingFlags.Static | BindingFlags.NonPublic)!;
        var lambda = Expression.Lambda(handler.PropertyType,
            Expression.Call(capture, Expression.Convert(parameter, typeof(object))), parameter).Compile();
        handler.SetValue(null, lambda);
        try
        {
            Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(210, 9), button = 0 }, 320, 40,
                () => EditorGUI.EnumPopup(new Rect(0, 0, 320, 18), "Mode", ProbeMode.Second));
            Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(210, 9), button = 0 }, 320, 40,
                () => EditorGUI.EnumPopup(new Rect(0, 0, 320, 18), "Mode", ProbeMode.Second));
            var items = ((IEnumerable?)_capturedMenuItems)?.Cast<object>().ToArray() ?? [];
            Require(items.Length == 3, "EnumPopup did not create one dropdown item per enum value.");
            Require(items.Count(item => (bool)item.GetType().GetProperty("On")!.GetValue(item)!) == 1,
                "EnumPopup did not mark its current item as checked.");
            Require(items.Select(item => (string)item.GetType().GetProperty("Path")!.GetValue(item)!)
                    .SequenceEqual(["First", "Second", "Third"]),
                "EnumPopup dropdown labels do not match the enum names.");
            Require(!_capturedMenuAdvanced,
                "A short EnumPopup unexpectedly opened as an AdvancedDropdown.");

            _capturedMenuItems = null;
            _capturedMenuAdvanced = false;
            var longOptions = Enumerable.Range(0, 20).Select(index => $"Option {index:00}").ToArray();
            Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(210, 9), button = 0 },
                320, 40, () => EditorGUI.Popup(new Rect(0, 0, 320, 18), "Long", 0, longOptions));
            Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(210, 9), button = 0 },
                320, 40, () => EditorGUI.Popup(new Rect(0, 0, 320, 18), "Long", 0, longOptions));
            Require(_capturedMenuAdvanced &&
                    ((IEnumerable?)_capturedMenuItems)?.Cast<object>().Count() == longOptions.Length,
                "A long EditorGUI.Popup did not automatically open as an AdvancedDropdown.");
        }
        finally { handler.SetValue(null, null); }
    }

    private static void VerifyRangeSlider()
    {
        var probe = ScriptableObject.CreateInstance<InspectorProbe>();
        using var serialized = new SerializedObject(probe);
        var property = serialized.FindProperty(nameof(InspectorProbe.speed))!;
        var commands = Render(340, 40, () => EditorGUI.PropertyField(new Rect(0, 0, 340, 18), property));
        Require(commands.Count(command => command.Type == GpuCanvasCommandType.SolidRect) >= 3,
            "[Range] did not render a slider track and thumb.");

        Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(210, 9), button = 0 }, 340, 40,
            () => EditorGUI.PropertyField(new Rect(0, 0, 340, 18), property));
        Require(probe.speed > 3 && probe.speed < 8,
            $"Dragging the [Range] field did not update its numeric value: {probe.speed}.");
        Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(210, 9), button = 0 }, 340, 40,
            () => EditorGUI.PropertyField(new Rect(0, 0, 340, 18), property));
    }

    private static void VerifyObjectFields()
    {
        var dispatcher = typeof(EditorWindow).Assembly.GetType("BEngine.Editor.GenericMenuDispatcher", true)!;
        var handler = dispatcher.GetProperty("Handler", BindingFlags.Static | BindingFlags.NonPublic)!;
        var invoke = handler.PropertyType.GetMethod("Invoke")!;
        var parameterType = invoke.GetParameters()[0].ParameterType;
        var parameter = Expression.Parameter(parameterType, "items");
        var capture = typeof(Program).GetMethod(nameof(CaptureMenu), BindingFlags.Static | BindingFlags.NonPublic)!;
        handler.SetValue(null, Expression.Lambda(handler.PropertyType,
            Expression.Call(capture, Expression.Convert(parameter, typeof(object))), parameter).Compile());

        using var scene = new Scene("Object Field Scene");
        var compatible = scene.CreateGameObject("Object Field Compatible");
        var cameraObject = scene.CreateGameObject("Object Field Camera");
        var camera = cameraObject.AddComponent<Camera2D>();
        camera.name = "Object Field Camera Component";
        var incompatible = new Material(Shader.Find("Tests/ObjectField")) { name = "Object Field Incompatible" };
        var rect = new Rect(0, 0, 320, 18);

        try
        {
            Selection.activeObject = incompatible;
            OpenObjectMenu(rect, null, typeof(GameObject), allowSceneObjects: true);
            Require(_capturedMenuAdvanced, "ObjectField did not use an AdvancedDropdown picker.");
            Require(CapturedMenuItems().Any(item => MenuPath(item).Equals("None", StringComparison.OrdinalIgnoreCase)),
                "ObjectField picker does not provide a None entry.");
            Require(!CapturedMenuItems().Any(item => IsSelectionItem(item) && MenuEnabled(item)),
                "ObjectField exposed an incompatible active Selection.");

            Selection.activeObject = compatible;
            OpenObjectMenu(rect, null, typeof(GameObject), allowSceneObjects: true);
            var selectionItem = CapturedMenuItems().Single(item => IsSelectionItem(item) && MenuEnabled(item));
            InvokeMenuItem(selectionItem);
            var selected = RenderObjectField(rect, null, typeof(GameObject), allowSceneObjects: true);
            Require(ReferenceEquals(selected, compatible),
                "ObjectField did not return its compatible Selection on the next IMGUI pass.");

            OpenObjectMenu(rect, selected, typeof(GameObject), allowSceneObjects: true);
            InvokeMenuItem(CapturedMenuItems().Single(item =>
                MenuPath(item).Equals("None", StringComparison.OrdinalIgnoreCase)));
            selected = RenderObjectField(rect, selected, typeof(GameObject), allowSceneObjects: true);
            Require(selected is null, "ObjectField None did not clear the current reference.");

            var equivalentMaterial = new Material(Shader.Find("Tests/ObjectFieldEquivalent"))
            {
                name = "Object Field Equivalent"
            };
            typeof(BObject).GetProperty(nameof(BObject.Id))!.SetValue(equivalentMaterial, incompatible.Id);
            Selection.activeObject = equivalentMaterial;
            OpenObjectMenu(rect, incompatible, typeof(Material), allowSceneObjects: true);
            InvokeMenuItem(CapturedMenuItems().Single(item => IsSelectionItem(item) && MenuEnabled(item)));
            BObject? unchangedAsset = null;
            var reportedAssetChange = true;
            Dispatch(new Event(EventType.Repaint), 320, 40, () =>
            {
                unchangedAsset = EditorGUI.ObjectField(
                    rect, "Target", incompatible, typeof(Material), allowSceneObjects: true);
                reportedAssetChange = GUI.changed;
            });
            Require(ReferenceEquals(unchangedAsset, incompatible) && !reportedAssetChange,
                "Selecting a stable-equivalent asset produced a redundant ObjectField change.");

            var probe = ScriptableObject.CreateInstance<InspectorProbe>();
            using (var serialized = new SerializedObject(probe))
            {
                var property = serialized.FindProperty(nameof(InspectorProbe.gameObjectReference))!;
                var rejectedIncompatibleReference = false;
                try { property.objectReferenceValue = incompatible; }
                catch (ArgumentException) { rejectedIncompatibleReference = true; }
                Require(rejectedIncompatibleReference && !serialized.hasModifiedProperties &&
                        probe.gameObjectReference is null,
                    "SerializedProperty accepted an object reference incompatible with its declared type.");

                Selection.activeObject = compatible;
                OpenSerializedObjectMenu(rect, property, typeof(GameObject), allowSceneObjects: true);
                InvokeMenuItem(CapturedMenuItems().Single(item => IsSelectionItem(item) && MenuEnabled(item)));
                Dispatch(new Event(EventType.Repaint), 320, 40,
                    () => EditorGUI.ObjectField(rect, property, typeof(GameObject), allowSceneObjects: true));
                Require(serialized.hasModifiedProperties && ReferenceEquals(probe.gameObjectReference, compatible),
                    "SerializedProperty ObjectField did not write the chosen object through SerializedObject.");
                Require(serialized.ApplyModifiedProperties(),
                    "SerializedProperty ObjectField did not leave an applicable serialized change.");
            }

            var layoutProbe = ScriptableObject.CreateInstance<InspectorProbe>();
            using (var layoutSerialized = new SerializedObject(layoutProbe))
            {
                var layoutProperty = layoutSerialized.FindProperty(nameof(InspectorProbe.gameObjectReference))!;
                var layoutCommands = Render(420, 100, () =>
                {
                    _ = EditorGUILayout.ObjectField("Layout Object", compatible, typeof(GameObject), true);
                    _ = EditorGUILayout.ObjectField<GameObject>("Layout Generic", compatible, true);
                    EditorGUILayout.ObjectField(layoutProperty, typeof(GameObject), true);
                });
                Require(layoutCommands.Count(command => command.Type == GpuCanvasCommandType.Text &&
                                                        command.Content.Contains("Object Field Compatible",
                                                            StringComparison.Ordinal)) >= 2,
                    "EditorGUILayout ObjectField overloads did not render their assigned objects.");
                Require(layoutCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                      command.Content.Contains("None (Game Object)",
                                                          StringComparison.Ordinal)),
                    "EditorGUILayout SerializedProperty ObjectField overload did not render its value.");
            }

            var mixedFirst = ScriptableObject.CreateInstance<InspectorProbe>();
            var mixedSecond = ScriptableObject.CreateInstance<InspectorProbe>();
            mixedFirst.gameObjectReference = compatible;
            mixedSecond.gameObjectReference = cameraObject;
            using (var mixedSerialized = new SerializedObject([mixedFirst, mixedSecond]))
            {
                var mixedProperty = mixedSerialized.FindProperty(nameof(InspectorProbe.gameObjectReference))!;
                Require(mixedProperty.hasMultipleDifferentValues,
                    "Object-reference SerializedProperty did not detect mixed target values.");
                var mixedCommands = Render(320, 40,
                    () => EditorGUI.PropertyField(rect, mixedProperty));
                Require(mixedCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                     command.Content == "-"),
                    "A mixed ObjectField did not render its mixed-value indicator.");

                Selection.activeObject = compatible;
                OpenSerializedObjectMenu(rect, mixedProperty, typeof(GameObject), allowSceneObjects: true);
                InvokeMenuItem(CapturedMenuItems().Single(item => IsSelectionItem(item) && MenuEnabled(item)));
                Dispatch(new Event(EventType.Repaint), 320, 40,
                    () => EditorGUI.ObjectField(rect, mixedProperty, typeof(GameObject), true));
                Require(ReferenceEquals(mixedFirst.gameObjectReference, compatible) &&
                        ReferenceEquals(mixedSecond.gameObjectReference, compatible) &&
                        !mixedProperty.hasMultipleDifferentValues,
                    "Choosing the first mixed ObjectField value did not write it to every target.");
                Require(mixedSerialized.ApplyModifiedProperties(),
                    "Mixed ObjectField unification did not leave an applicable serialized change.");
            }

            BObject? dragged = null;
            DragAndDrop.objectReferences = [compatible];
            Dispatch(new Event(EventType.DragUpdated) { mousePosition = new Vector2(210, 9) }, 320, 40,
                () => dragged = EditorGUI.ObjectField(rect, "Target", dragged, typeof(GameObject), true));
            Require(DragAndDrop.visualMode != DragAndDropVisualMode.Rejected,
                "ObjectField rejected a compatible dragged GameObject.");
            Dispatch(new Event(EventType.DragPerform) { mousePosition = new Vector2(210, 9) }, 320, 40,
                () => dragged = EditorGUI.ObjectField(rect, "Target", dragged, typeof(GameObject), true));
            Require(ReferenceEquals(dragged, compatible),
                "ObjectField did not accept a compatible dragged GameObject.");

            DragAndDrop.objectReferences = [incompatible];
            Dispatch(new Event(EventType.DragUpdated) { mousePosition = new Vector2(210, 9) }, 320, 40,
                () => dragged = EditorGUI.ObjectField(rect, "Target", dragged, typeof(GameObject), true));
            Require(DragAndDrop.visualMode == DragAndDropVisualMode.Rejected,
                "ObjectField did not reject a type-incompatible dragged asset.");
            Dispatch(new Event(EventType.DragPerform) { mousePosition = new Vector2(210, 9) }, 320, 40,
                () => dragged = EditorGUI.ObjectField(rect, "Target", dragged, typeof(GameObject), true));
            Require(ReferenceEquals(dragged, compatible),
                "A rejected drag replaced the ObjectField value.");

            BObject? asset = null;
            DragAndDrop.objectReferences = [incompatible];
            Dispatch(new Event(EventType.DragUpdated) { mousePosition = new Vector2(210, 9) }, 320, 40,
                () => asset = EditorGUI.ObjectField(rect, "Asset", asset, typeof(BAsset), false));
            Require(DragAndDrop.visualMode == DragAndDropVisualMode.Rejected && asset is null,
                "An asset-only ObjectField accepted a non-persistent BAsset.");
            DragAndDrop.objectReferences = [camera];
            Dispatch(new Event(EventType.DragUpdated) { mousePosition = new Vector2(210, 9) }, 320, 40,
                () => asset = EditorGUI.ObjectField(rect, "Asset", asset, typeof(BAsset), false));
            Require(DragAndDrop.visualMode == DragAndDropVisualMode.Rejected && asset is null,
                "An asset-only ObjectField accepted a scene Component.");
        }
        finally
        {
            Selection.activeObject = null;
            DragAndDrop.objectReferences = [];
            handler.SetValue(null, null);
        }
    }

    private static void OpenObjectMenu(Rect rect, BObject? value, Type type, bool allowSceneObjects)
    {
        _capturedMenuItems = null;
        _capturedMenuAdvanced = false;
        Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(210, 9), button = 0 }, 320, 40,
            () => EditorGUI.ObjectField(rect, "Target", value, type, allowSceneObjects));
        Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(210, 9), button = 0 }, 320, 40,
            () => EditorGUI.ObjectField(rect, "Target", value, type, allowSceneObjects));
        Require(_capturedMenuItems is not null, "Clicking ObjectField did not open its picker.");
    }

    private static void OpenSerializedObjectMenu(
        Rect rect, SerializedProperty property, Type type, bool allowSceneObjects)
    {
        _capturedMenuItems = null;
        _capturedMenuAdvanced = false;
        Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(210, 9), button = 0 }, 320, 40,
            () => EditorGUI.ObjectField(rect, property, type, allowSceneObjects));
        Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(210, 9), button = 0 }, 320, 40,
            () => EditorGUI.ObjectField(rect, property, type, allowSceneObjects));
        Require(_capturedMenuItems is not null, "Clicking SerializedProperty ObjectField did not open its picker.");
    }

    private static BObject? RenderObjectField(Rect rect, BObject? value, Type type, bool allowSceneObjects)
    {
        BObject? result = value;
        Dispatch(new Event(EventType.Repaint), 320, 40,
            () => result = EditorGUI.ObjectField(rect, "Target", value, type, allowSceneObjects));
        return result;
    }

    private static object[] CapturedMenuItems() =>
        ((IEnumerable?)_capturedMenuItems)?.Cast<object>().ToArray() ?? [];

    private static string MenuPath(object item) =>
        (string?)item.GetType().GetProperty("Path")?.GetValue(item) ?? string.Empty;

    private static bool MenuEnabled(object item) =>
        item.GetType().GetProperty("Enabled")?.GetValue(item) is true;

    private static bool IsSelectionItem(object item) =>
        MenuPath(item).Contains("Select", StringComparison.OrdinalIgnoreCase);

    private static void InvokeMenuItem(object item)
    {
        var action = item.GetType().GetProperty("Action")?.GetValue(item) as Action;
        Require(action is not null, $"Object picker item '{MenuPath(item)}' has no action.");
        action!();
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

    private static void CaptureMenu(object items)
    {
        _capturedMenuItems = items;
        var dispatcher = typeof(EditorWindow).Assembly.GetType(
            "BEngine.Editor.GenericMenuDispatcher", true)!;
        var presentation = dispatcher.GetProperty(
            "CurrentPresentation", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        _capturedMenuAdvanced = (bool)(presentation.GetType().GetProperty(
            "IsAdvanced", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(presentation) ?? false);
    }

    private static bool RectsOverlap(GpuCanvasRect left, GpuCanvasRect right) =>
        left.X < right.Right && left.Right > right.X && left.Y < right.Bottom && left.Bottom > right.Y;

    private static void VerifyAxisFieldOrdering(IReadOnlyList<GpuCanvasCommand> texts,
        IReadOnlyList<string> values)
    {
        VerifyAxisFieldOrdering(texts, ["X", "Y", "Z"], values);
    }

    private static void VerifyAxisFieldOrdering(IReadOnlyList<GpuCanvasCommand> texts,
        IReadOnlyList<string> axes, IReadOnlyList<string> values)
    {
        var axisCommands = axes
            .Select(axis => texts.Single(command => command.Content == axis)).ToArray();
        var fields = values.Select(value => texts.Single(command => command.Content == value)).ToArray();
        for (var index = 0; index < axisCommands.Length; index++)
        {
            var requiredAxisWidth = (float)EditorStyles.vectorAxisLabel
                .CalcSize(new GUIContent(axisCommands[index].Content)).x;
            Require(axisCommands[index].Rect.Width + 0.01f >= requiredAxisWidth,
                $"Axis {axisCommands[index].Content} label is clipped: " +
                $"{axisCommands[index].Rect.Width} < {requiredAxisWidth}.");
            Require(axisCommands[index].Rect.Right <= fields[index].Rect.X,
                $"Axis {axisCommands[index].Content} overlaps its numeric field.");
            Require(fields[index].Rect.Width >= 58,
                $"Axis {axisCommands[index].Content} numeric field is too narrow: {fields[index].Rect.Width}; " +
                $"axis={axisCommands[index].Rect.X},{axisCommands[index].Rect.Width}; " +
                $"value={fields[index].Rect.X},{fields[index].Rect.Right}.");
            if (index + 1 < axisCommands.Length &&
                Math.Abs((float)(axisCommands[index].Rect.Y - axisCommands[index + 1].Rect.Y)) < 0.01f)
                Require(fields[index].Rect.Right <= axisCommands[index + 1].Rect.X,
                    $"Axis {axisCommands[index].Content} field overlaps axis {axisCommands[index + 1].Content}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

}
