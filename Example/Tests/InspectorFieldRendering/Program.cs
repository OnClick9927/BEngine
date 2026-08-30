using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using BEngine;
using BEngine.Editor;
using BEngine.Editor.Rendering;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;

namespace BEngine.ExampleTests.InspectorFieldRendering;

internal static class Program
{
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod("BeginFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod("EndFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo GetVectorFieldHeight = typeof(EditorGUI).GetMethod(
        "GetVectorFieldHeight", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static object? _capturedMenuItems;
    private static bool _capturedMenuAdvanced;
    private static object? _capturedObjectPickerRequest;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--object-picker-only", StringComparer.OrdinalIgnoreCase))
            {
                VerifyTabbedObjectPicker();
                Console.WriteLine("OBJECT_PICKER_TREE_OK|assets-scene-tabs,assets-root-hidden," +
                                  "standard-foldout,search-tree,mouse-exit,token-isolation");
                return 0;
            }
            VerifyNarrowVectorAndPrecision();
            VerifyVector4ResponsiveLayout();
            VerifyNarrowSerializedComponentFields();
            VerifyIndentedVectorLayout();
            VerifyNestedLabelContentLayout();
            VerifyClippedInspectorVectorLayout();
            VerifyHighDpiVectorLayout();
            VerifyColorSwatchAndAlphaPicker();
            VerifyEnumDropdown();
            VerifyNestedObjectInspector();
            VerifyObjectFields();
            VerifyObjectFieldIsolation();
            VerifyTabbedObjectPicker();
            VerifyRangeSlider();
            Console.WriteLine(
                "INSPECTOR_FIELD_RENDERING_OK|vector24-responsive,vector4dp,color-alpha,enum-dropdown," +
                "advanced-popup,object-field,object-dragdrop,range-slider,nested-object-foldout," +
                "object-picker-tabs,object-picker-token-isolation," +
                "nested-indent,null,array-list,cycle-depth,foldout-isolation,getter-fault-isolation," +
                "indented-vector-bounds,indented-vector-responsive,nested-centered-label," +
                "nested-image-spacing");
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

    private static void VerifyIndentedVectorLayout()
    {
        var twoLines = EditorGUIUtility.singleLineHeight * 2 +
                       EditorGUIUtility.standardVerticalSpacing;
        var vector2Stacked = EditorGUIUtility.singleLineHeight * 3 +
                             EditorGUIUtility.standardVerticalSpacing * 2;
        var vector4Stacked = EditorGUIUtility.singleLineHeight * 5 +
                             EditorGUIUtility.standardVerticalSpacing * 4;

        var vector2FlatHeight = ResolveVectorFieldHeight(viewportWidth: 190, dimensions: 2, indentLevel: 0);
        var vector2IndentedHeight = ResolveVectorFieldHeight(viewportWidth: 190, dimensions: 2, indentLevel: 1);
        Require(vector2FlatHeight == twoLines && vector2IndentedHeight == vector2Stacked,
            $"Vector2 responsive height did not deduct one indent unit: " +
            $"flat={vector2FlatHeight}, indented={vector2IndentedHeight}.");

        var vector4FlatHeight = ResolveVectorFieldHeight(viewportWidth: 370, dimensions: 4, indentLevel: 0);
        var vector4IndentedHeight = ResolveVectorFieldHeight(viewportWidth: 370, dimensions: 4, indentLevel: 1);
        Require(vector4FlatHeight == twoLines && vector4IndentedHeight == vector4Stacked,
            $"Vector4 responsive height did not deduct one indent unit: " +
            $"flat={vector4FlatHeight}, indented={vector4IndentedHeight}.");

        var vector2Rect = new Rect(37, 9, 126, vector2Stacked);
        var vector2Commands = Render(240, 120, () =>
        {
            var previousIndent = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 2;
                EditorGUI.Vector2Field(vector2Rect, "Nested Offset",
                    new Vector2(Fix64.FromDecimal(11.25m), Fix64.FromDecimal(-22.5m)));
            }
            finally { EditorGUI.indentLevel = previousIndent; }
        });
        RequireCommandsInside(vector2Commands, vector2Rect, "Indented narrow Vector2");

        var vector4Rect = new Rect(53, 7, 190, vector4Stacked);
        var vector4Commands = Render(320, 140, () =>
        {
            var previousIndent = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 2;
                EditorGUI.Vector4Field(vector4Rect, "Nested Weights",
                    new Vector4(1, 2, 3, 4));
            }
            finally { EditorGUI.indentLevel = previousIndent; }
        });
        RequireCommandsInside(vector4Commands, vector4Rect, "Indented narrow Vector4");
    }

    private static Fix64 ResolveVectorFieldHeight(int viewportWidth, int dimensions, int indentLevel)
    {
        var result = Fix64.Zero;
        Dispatch(new Event(EventType.Layout), viewportWidth, 180, () =>
        {
            var previousIndent = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = indentLevel;
                result = (Fix64)GetVectorFieldHeight.Invoke(null, [dimensions])!;
            }
            finally { EditorGUI.indentLevel = previousIndent; }
        });
        return result;
    }

    private static void VerifyNestedLabelContentLayout()
    {
        var centeredRect = new Rect(24, 5, 240, 22);
        var centeredStyle = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter
        };
        var centeredCommands = Render(320, 40, () =>
        {
            var previousIndent = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 1;
                EditorGUI.LabelField(centeredRect, new GUIContent("Centered Child"), centeredStyle);
            }
            finally { EditorGUI.indentLevel = previousIndent; }
        });
        var centeredText = centeredCommands.Single(command =>
            command.Type == GpuCanvasCommandType.Text && command.Content == "Centered Child");
        const float indentWidth = 15;
        var expectedCenter = ((float)centeredRect.x + indentWidth + (float)centeredRect.xMax) * 0.5f;
        var actualCenter = (centeredText.Rect.X + centeredText.Rect.Right) * 0.5f;
        Require(Math.Abs(actualCenter - expectedCenter) <= 0.1f,
            $"A centered nested LabelField was shifted by virtual foldout space: " +
            $"center={actualCenter:0.###}, expected={expectedCenter:0.###}.");

        var iconRect = new Rect(31, 7, 250, 22);
        var iconContent = new GUIContent("Icon Child", "Icons/Tests/NestedLabel.png", string.Empty);
        var iconCommands = Render(340, 44, () =>
        {
            var previousIndent = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 1;
                EditorGUI.LabelField(iconRect, iconContent, EditorStyles.label);
            }
            finally { EditorGUI.indentLevel = previousIndent; }
        });
        var image = iconCommands.Single(command => command.Type == GpuCanvasCommandType.Image &&
                                                   command.Content == iconContent.image);
        var text = iconCommands.Single(command => command.Type == GpuCanvasCommandType.Text &&
                                                  command.Content == iconContent.text);
        var indentedLeft = (float)iconRect.x + indentWidth;
        Require(Math.Abs(image.Rect.X - (indentedLeft + 3)) <= 0.1f,
            $"A nested GUIContent image did not start inside its indented row: x={image.Rect.X:0.###}.");
        Require(Math.Abs(text.Rect.X - (indentedLeft + 22)) <= 0.1f,
            $"A nested GUIContent image reserved its 22px text spacing more than once: " +
            $"x={text.Rect.X:0.###}, expected={indentedLeft + 22:0.###}.");
    }

    private static void RequireCommandsInside(IEnumerable<GpuCanvasCommand> commands, Rect bounds, string name)
    {
        var left = (float)bounds.x;
        var top = (float)bounds.y;
        var right = (float)bounds.xMax;
        var bottom = (float)bounds.yMax;
        foreach (var command in commands)
            Require(command.Rect.X >= left - 0.1f && command.Rect.Y >= top - 0.1f &&
                    command.Rect.Right <= right + 0.1f && command.Rect.Bottom <= bottom + 0.1f,
                $"{name} command '{command.Type}:{command.Content}' escaped its Rect: " +
                $"({command.Rect.X:0.###},{command.Rect.Y:0.###})-" +
                $"({command.Rect.Right:0.###},{command.Rect.Bottom:0.###}) outside " +
                $"({left:0.###},{top:0.###})-({right:0.###},{bottom:0.###}).");
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
            Require(!_capturedMenuAdvanced &&
                    ((IEnumerable?)_capturedMenuItems)?.Cast<object>().Count() == longOptions.Length,
                "A long EditorGUI.Popup implicitly changed from a system DropDown to an AdvancedDropdown.");

            _capturedMenuItems = null;
            _capturedMenuAdvanced = false;
            Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(210, 9), button = 0 },
                320, 40, () => EditorGUI.AdvancedPopup(
                    new Rect(0, 0, 320, 18), "Advanced", 0, longOptions));
            Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(210, 9), button = 0 },
                320, 40, () => EditorGUI.AdvancedPopup(
                    new Rect(0, 0, 320, 18), "Advanced", 0, longOptions));
            Require(_capturedMenuAdvanced &&
                    ((IEnumerable?)_capturedMenuItems)?.Cast<object>().Count() == longOptions.Length,
                "EditorGUI.AdvancedPopup did not explicitly open as an AdvancedDropdown.");
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

    private static void VerifyNestedObjectInspector()
    {
        using var scene = new Scene("Nested Inspector Scene");
        var gameObject = scene.CreateGameObject("Nested Inspector Probe");
        var component = gameObject.AddComponent<NestedInspectorComponent>();
        using var serialized = new SerializedObject(component);
        var a = serialized.FindProperty(nameof(NestedInspectorComponent.a))!;
        var b = serialized.FindProperty(nameof(NestedInspectorComponent.b))!;
        var nullable = serialized.FindProperty(nameof(NestedInspectorComponent.nullable))!;

        Require(a.propertyType == SerializedPropertyType.Generic && a.hasVisibleChildren,
            "A serializable ordinary object was not exposed as an expandable Generic property.");
        Require(!nullable.hasVisibleChildren,
            "A null ordinary object incorrectly reported visible children.");
        var nullCommands = Render(420, 50,
            () => EditorGUI.PropertyField(new Rect(0, 0, 420, 22), nullable, includeChildren: true));
        Require(nullCommands.Any(command => command.Type == GpuCanvasCommandType.Text && command.Content == "None"),
            "A null ordinary object was not rendered safely as None.");

        ClickFoldout(a, new Rect(0, 0, 420, 22));
        Require(a.isExpanded && !b.isExpanded,
            "Expanding one nested field leaked its foldout state to a sibling field.");
        using (var otherSerialized = new SerializedObject(component))
            Require(!otherSerialized.FindProperty(nameof(NestedInspectorComponent.a))!.isExpanded,
                "Nested foldout state leaked to another SerializedObject instance.");

        var expandedHeight = EditorGUI.GetPropertyHeight(a, includeChildren: true);
        Require(expandedHeight > EditorGUIUtility.singleLineHeight,
            "Expanded nested object height did not include its child fields.");
        var expanded = Render(420, (int)Math.Ceiling((double)expandedHeight) + 8,
            () => EditorGUI.PropertyField(new Rect(0, 0, 420, expandedHeight), a, includeChildren: true));
        var ageLabel = expanded.Single(command => command.Type == GpuCanvasCommandType.Text &&
                                                  command.Content == "Age");
        Require(ageLabel.Rect.X >= 14,
            $"Nested field indentation is not proportional to depth 1: x={ageLabel.Rect.X}.");
        Require(!expanded.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                         command.Content == "Hidden"),
            "[HideInInspector] was ignored inside an ordinary nested object.");
        Require(expanded.Count(command => command.Type == GpuCanvasCommandType.Text &&
                                           command.Content == "Experience") == 1 &&
                !expanded.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                         command.Content.Contains("Backing", StringComparison.Ordinal)),
            "A serialized auto-property exposed its compiler-generated backing field or rendered twice.");

        var age = serialized.FindProperty($"{nameof(NestedInspectorComponent.a)}.age")!;
        var experience = serialized.FindProperty($"{nameof(NestedInspectorComponent.a)}.experience")!;
        Require(age.depth == 1, $"Nested ordinary field depth was {age.depth}, expected 1.");
        age.intValue = 41;
        experience.intValue = 43;
        Require(serialized.ApplyModifiedProperties() && component.a.age == 41 && component.a.experience == 43,
            "A nested field/property edit was not applied to the owning Component.");

        VerifyNestedCollection(serialized, component, nameof(NestedInspectorComponent.items), isList: false);
        VerifyNestedCollection(serialized, component, nameof(NestedInspectorComponent.entries), isList: true);
        VerifyNullNestedCollection(serialized, component,
            nameof(NestedInspectorComponent.optionalItems), isList: false);
        VerifyNullNestedCollection(serialized, component,
            nameof(NestedInspectorComponent.optionalEntries), isList: true);
        VerifyNestedCycleAndDepth(serialized);
        VerifyThrowingGetterIsolation(serialized);
        Require(GUIUtility.hotControl == 0,
            $"Nested Inspector drawing leaked hot control {GUIUtility.hotControl}.");
    }

    private static void VerifyThrowingGetterIsolation(SerializedObject serialized)
    {
        var throwing = serialized.FindProperty(nameof(NestedInspectorComponent.throwing))!;
        var following = serialized.FindProperty(nameof(NestedInspectorComponent.zAfterThrow))!;
        throwing.isExpanded = true;
        Require(EditorGUI.GetPropertyHeight(throwing, includeChildren: true) ==
                EditorGUIUtility.singleLineHeight,
            "A throwing getter corrupted nested property height calculation.");

        var stateRestored = false;
        var commands = Render(420, 60, () =>
        {
            EditorGUI.indentLevel = 2;
            GUI.enabled = true;
            EditorGUI.PropertyField(new Rect(0, 0, 420, 18), throwing, includeChildren: true);
            stateRestored = EditorGUI.indentLevel == 2 && GUI.enabled;
            EditorGUI.PropertyField(new Rect(0, 22, 420, 18), following);
            EditorGUI.indentLevel = 0;
        });

        Require(stateRestored, "A throwing getter leaked GUI enabled or indentation state.");
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                        command.Content == "Unavailable"),
            "A throwing getter did not render an isolated unavailable value.");
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                        command.Content == "Z After Throw") &&
                commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                        command.Content == "97"),
            "A throwing getter prevented the following Inspector field from rendering.");
    }

    private static void VerifyNestedCollection(SerializedObject serialized, NestedInspectorComponent component,
        string propertyName, bool isList)
    {
        var collection = serialized.FindProperty(propertyName)!;
        Require(collection.isArray && collection.hasVisibleChildren,
            $"Nested collection '{propertyName}' did not expose its elements.");
        collection.isExpanded = true;
        var element = serialized.FindProperty($"{propertyName}[0]")!;
        element.isExpanded = true;
        Require(element.displayName == "Element 0" && element.depth == 1,
            $"Collection element metadata was not normalized: {element.displayName}, depth {element.depth}.");
        var elementAge = serialized.FindProperty($"{propertyName}[0].age")!;
        Require(elementAge.depth == 2,
            $"Collection child depth was {elementAge.depth}, expected 2.");
        elementAge.intValue = isList ? 73 : 71;
        Require(serialized.ApplyModifiedProperties(),
            $"Editing '{propertyName}[0].age' did not create an applicable serialized change.");
        var actual = isList ? component.entries[0].age : component.items[0].age;
        Require(actual == (isList ? 73 : 71),
            $"Editing '{propertyName}[0].age' did not update the collection element.");

        var height = EditorGUI.GetPropertyHeight(collection, includeChildren: true);
        var commands = Render(420, (int)Math.Ceiling((double)height) + 8,
            () => EditorGUI.PropertyField(new Rect(0, 0, 420, height), collection, includeChildren: true));
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                        command.Content == "Element 0") &&
                commands.Any(command => command.Type == GpuCanvasCommandType.Text && command.Content == "Age"),
            $"Expanded collection '{propertyName}' did not draw its element and nested fields.");
        var ageLabel = commands.Single(command => command.Type == GpuCanvasCommandType.Text &&
                                                  command.Content == "Age");
        Require(ageLabel.Rect.X >= 29,
            $"Collection child indentation is not proportional to depth 2: x={ageLabel.Rect.X}.");
    }

    private static void VerifyNullNestedCollection(SerializedObject serialized,
        NestedInspectorComponent component, string propertyName, bool isList)
    {
        var collection = serialized.FindProperty(propertyName)!;
        Require(collection.isArray && collection.arraySize == 0,
            $"Null declared collection '{propertyName}' was not recognized by SerializedProperty.");
        collection.isExpanded = true;
        var height = EditorGUI.GetPropertyHeight(collection, includeChildren: true);
        var commands = Render(420, (int)Math.Ceiling((double)height) + 8,
            () => EditorGUI.PropertyField(new Rect(0, 0, 420, height), collection, includeChildren: true));
        var list = ReorderableList.GetReorderableListFromSerializedProperty(collection);
        Require(list is not null && commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                             command.Content == "+"),
            $"Null declared collection '{propertyName}' did not use the default ReorderableList.");

        ReorderableList.defaultBehaviours.DoAddButton(list!);
        Require(serialized.ApplyModifiedPropertiesWithoutUndo(),
            $"Null declared collection '{propertyName}' initialization was not applicable.");
        var initializedCount = isList ? component.optionalEntries?.Count : component.optionalItems?.Length;
        Require(initializedCount == 1,
            $"Null declared collection '{propertyName}' was not initialized by the list add action.");
    }

    private static void VerifyNestedCycleAndDepth(SerializedObject serialized)
    {
        var cycle = serialized.FindProperty(nameof(NestedInspectorComponent.cycle))!;
        var cycleNext = serialized.FindProperty($"{nameof(NestedInspectorComponent.cycle)}.next")!;
        cycle.isExpanded = true;
        cycleNext.isExpanded = true;
        var cycleHeight = EditorGUI.GetPropertyHeight(cycle, includeChildren: true);
        var maximumCycleHeight = EditorGUIUtility.singleLineHeight * 3 +
                                 EditorGUIUtility.standardVerticalSpacing * 2;
        Require(cycleHeight <= maximumCycleHeight,
            $"Circular ordinary object recursion was not bounded: {cycleHeight}.");
        _ = Render(420, 160,
            () => EditorGUI.PropertyField(new Rect(0, 0, 420, cycleHeight), cycle, includeChildren: true));

        var deep = serialized.FindProperty(nameof(NestedInspectorComponent.deep))!;
        var path = nameof(NestedInspectorComponent.deep);
        for (var depth = 0; depth < 14; depth++)
        {
            serialized.FindProperty(path)!.isExpanded = true;
            path += ".next";
        }
        var maximumExpected = EditorGUIUtility.singleLineHeight * 9 +
                              EditorGUIUtility.standardVerticalSpacing * 8;
        var deepHeight = EditorGUI.GetPropertyHeight(deep, includeChildren: true);
        Require(deepHeight <= maximumExpected,
            $"Nested object maximum depth was not enforced: {deepHeight} > {maximumExpected}.");
        _ = Render(420, (int)Math.Ceiling((double)deepHeight) + 8,
            () => EditorGUI.PropertyField(new Rect(0, 0, 420, deepHeight), deep, includeChildren: true));
    }

    private static void ClickFoldout(SerializedProperty property, Rect rect)
    {
        Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(8, 9), button = 0 },
            (int)rect.width, 60, () => EditorGUI.PropertyField(rect, property, includeChildren: true));
        Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(8, 9), button = 0 },
            (int)rect.width, 60, () => EditorGUI.PropertyField(rect, property, includeChildren: true));
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
            Require(!_capturedMenuAdvanced,
                "The headless ObjectField fallback still routed through AdvancedDropdown.");
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

            BObject? draggedComponent = null;
            DragAndDrop.objectReferences = [cameraObject];
            Dispatch(new Event(EventType.DragUpdated) { mousePosition = new Vector2(210, 9) }, 320, 40,
                () => draggedComponent = EditorGUI.ObjectField(
                    rect, "Camera", draggedComponent, typeof(Camera2D), true));
            Require(DragAndDrop.visualMode == DragAndDropVisualMode.Link,
                "A Component ObjectField did not advertise a valid Hierarchy GameObject drop.");
            Dispatch(new Event(EventType.DragPerform) { mousePosition = new Vector2(210, 9) }, 320, 40,
                () => draggedComponent = EditorGUI.ObjectField(
                    rect, "Camera", draggedComponent, typeof(Camera2D), true));
            Require(ReferenceEquals(draggedComponent, camera),
                "A Component ObjectField did not resolve the matching component from a dragged GameObject.");

            BObject? draggedOwner = null;
            DragAndDrop.objectReferences = [camera];
            Dispatch(new Event(EventType.DragPerform) { mousePosition = new Vector2(210, 9) }, 320, 40,
                () => draggedOwner = EditorGUI.ObjectField(
                    rect, "Owner", draggedOwner, typeof(GameObject), true));
            Require(ReferenceEquals(draggedOwner, cameraObject),
                "A GameObject ObjectField did not resolve the owner of a dragged Component.");

            BObject? forbiddenSceneObject = null;
            DragAndDrop.objectReferences = [compatible];
            Dispatch(new Event(EventType.DragUpdated) { mousePosition = new Vector2(210, 9) }, 320, 40,
                () => forbiddenSceneObject = EditorGUI.ObjectField(
                    rect, "Asset Only", forbiddenSceneObject, typeof(GameObject), false));
            Require(DragAndDrop.visualMode == DragAndDropVisualMode.Rejected && forbiddenSceneObject is null,
                "ObjectField accepted a Hierarchy GameObject while allowSceneObjects was false.");

            var dragProbe = ScriptableObject.CreateInstance<InspectorProbe>();
            using (var dragSerialized = new SerializedObject(dragProbe))
            {
                var dragProperty = dragSerialized.FindProperty(nameof(InspectorProbe.gameObjectReference))!;
                DragAndDrop.objectReferences = [cameraObject];
                Dispatch(new Event(EventType.DragPerform) { mousePosition = new Vector2(210, 9) }, 320, 40,
                    () => EditorGUI.ObjectField(rect, dragProperty, typeof(GameObject), true));
                Require(ReferenceEquals(dragProbe.gameObjectReference, cameraObject) &&
                        dragSerialized.hasModifiedProperties && dragSerialized.ApplyModifiedProperties(),
                    "Dragging a Hierarchy object onto a SerializedProperty ObjectField did not commit the value.");
            }

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

            Selection.activeObject = incompatible;
            Dispatch(new Event(EventType.MouseDown)
                { mousePosition = new Vector2(210, 9), button = 0, clickCount = 1 }, 320, 40,
                () => EditorGUI.ObjectField(rect, "Target", compatible, typeof(GameObject), true));
            Dispatch(new Event(EventType.MouseUp)
                { mousePosition = new Vector2(210, 9), button = 0, clickCount = 1 }, 320, 40,
                () => EditorGUI.ObjectField(rect, "Target", compatible, typeof(GameObject), true));
            Require(ReferenceEquals(Selection.activeObject, incompatible),
                "A single ObjectField click selected the object instead of only pinging it.");

            Dispatch(new Event(EventType.MouseDown)
                { mousePosition = new Vector2(210, 9), button = 0, clickCount = 2 }, 320, 40,
                () => EditorGUI.ObjectField(rect, "Target", compatible, typeof(GameObject), true));
            Dispatch(new Event(EventType.MouseUp)
                { mousePosition = new Vector2(210, 9), button = 0, clickCount = 2 }, 320, 40,
                () => EditorGUI.ObjectField(rect, "Target", compatible, typeof(GameObject), true));
            Require(ReferenceEquals(Selection.activeObject, compatible),
                "A double ObjectField click did not select its BObject.");

            Selection.activeObject = incompatible;
            DragAndDrop.PrepareStartDrag();
            Dispatch(new Event(EventType.MouseDown)
                { mousePosition = new Vector2(210, 9), button = 0, clickCount = 1 }, 320, 40,
                () => EditorGUI.ObjectField(rect, "Target", compatible, typeof(GameObject), true));
            Dispatch(new Event(EventType.MouseDrag)
                { mousePosition = new Vector2(222, 9), button = 0 }, 320, 40,
                () => EditorGUI.ObjectField(rect, "Target", compatible, typeof(GameObject), true));
            Require(DragAndDrop.objectReferences is [var draggedReference] &&
                    ReferenceEquals(draggedReference, compatible),
                "Dragging an ObjectField did not publish its BObject through DragAndDrop.");
            Dispatch(new Event(EventType.MouseUp)
                { mousePosition = new Vector2(222, 9), button = 0, clickCount = 1 }, 320, 40,
                () => EditorGUI.ObjectField(rect, "Target", compatible, typeof(GameObject), true));
            Require(ReferenceEquals(Selection.activeObject, incompatible),
                "Completing an ObjectField drag also triggered its click Selection behavior.");
        }
        finally
        {
            Selection.activeObject = null;
            DragAndDrop.objectReferences = [];
            DragAndDrop.paths = [];
            handler.SetValue(null, null);
        }
    }

    private static void VerifyObjectFieldIsolation()
    {
        var dispatcher = typeof(EditorWindow).Assembly.GetType("BEngine.Editor.GenericMenuDispatcher", true)!;
        var handler = dispatcher.GetProperty("Handler", BindingFlags.Static | BindingFlags.NonPublic)!;
        var invoke = handler.PropertyType.GetMethod("Invoke")!;
        var parameterType = invoke.GetParameters()[0].ParameterType;
        var parameter = Expression.Parameter(parameterType, "items");
        var capture = typeof(Program).GetMethod(nameof(CaptureMenu), BindingFlags.Static | BindingFlags.NonPublic)!;
        handler.SetValue(null, Expression.Lambda(handler.PropertyType,
            Expression.Call(capture, Expression.Convert(parameter, typeof(object))), parameter).Compile());

        using var scene = new Scene("Object Field Isolation");
        var firstCandidate = scene.CreateGameObject("First Candidate");
        var secondCandidate = scene.CreateGameObject("Second Candidate");
        var firstRect = new Rect(0, 0, 320, 18);
        var secondRect = new Rect(0, 22, 320, 18);

        try
        {
            BObject? first = null;
            BObject? second = null;
            Selection.activeObject = secondCandidate;
            _capturedMenuItems = null;
            var reusedPointerEvent = new Event(EventType.MouseDown)
                { mousePosition = new Vector2(310, 31), button = 0 };
            Dispatch(reusedPointerEvent, 320, 64, DrawDifferentRects);
            reusedPointerEvent.type = EventType.MouseUp;
            reusedPointerEvent.rawType = EventType.MouseUp;
            Dispatch(reusedPointerEvent, 320, 64, DrawDifferentRects);
            Require(_capturedMenuItems is not null,
                "Reusing one Event instance across IMGUI passes changed ObjectField interaction ids.");
            InvokeMenuItem(CapturedMenuItems().Single(item => IsSelectionItem(item) && MenuEnabled(item)));
            Dispatch(new Event(EventType.Repaint), 320, 64, DrawDifferentRects);
            Require(first is null && ReferenceEquals(second, secondCandidate),
                "A picker selection from the second ObjectField was consumed by the first field.");

            first = null;
            second = null;
            Selection.activeObject = firstCandidate;
            _capturedMenuItems = null;
            Dispatch(new Event(EventType.MouseDown)
                { mousePosition = new Vector2(310, 9), button = 0 }, 320, 40, DrawOverlappingRects);
            Dispatch(new Event(EventType.MouseUp)
                { mousePosition = new Vector2(310, 9), button = 0 }, 320, 40, DrawOverlappingRects);
            InvokeMenuItem(CapturedMenuItems().Single(item => IsSelectionItem(item) && MenuEnabled(item)));
            Dispatch(new Event(EventType.Repaint), 320, 40, DrawOverlappingRects);
            Require(first is null && ReferenceEquals(second, firstCandidate),
                "Two same-label ObjectFields at the same Rect shared their pending picker selection.");

            first = null;
            second = null;
            DragAndDrop.objectReferences = [secondCandidate];
            Dispatch(new Event(EventType.DragUpdated)
                { mousePosition = new Vector2(210, 31) }, 320, 64, DrawDifferentRects);
            Require(first is null && second is null && DragAndDrop.visualMode == DragAndDropVisualMode.Link,
                "A compatible drag update leaked into a non-hovered ObjectField.");
            Dispatch(new Event(EventType.DragPerform)
                { mousePosition = new Vector2(210, 31) }, 320, 64, DrawDifferentRects);
            Require(first is null && ReferenceEquals(second, secondCandidate),
                "Dropping on the second ObjectField changed another field.");

            first = firstCandidate;
            second = secondCandidate;
            Selection.activeObject = null;
            ResetObjectPingForTests();
            var pinged = new List<BObject>();
            Action<BObject> onPing = target => pinged.Add(target);
            var pingEvent = typeof(EditorGUI).Assembly.GetType("BEngine.Editor.EditorObjectPing", true)!
                .GetEvent("pinged", BindingFlags.Static | BindingFlags.NonPublic)!;
            pingEvent.GetAddMethod(nonPublic: true)!.Invoke(null, [onPing]);
            try
            {
                Dispatch(new Event(EventType.MouseDown)
                    { mousePosition = new Vector2(210, 31), button = 0, clickCount = 1 },
                    320, 64, DrawDifferentRects);
                Dispatch(new Event(EventType.MouseUp)
                    { mousePosition = new Vector2(210, 31), button = 0, clickCount = 1 },
                    320, 64, DrawDifferentRects);
            }
            finally
            {
                pingEvent.GetRemoveMethod(nonPublic: true)!.Invoke(null, [onPing]);
            }
            Require(pinged.Count == 1 && ReferenceEquals(pinged[0], secondCandidate),
                "Clicking one ObjectField pinged another field's object.");

            var pulseCommands = Render(320, 64, DrawDifferentRects);
            var pulseEdges = pulseCommands.Where(command =>
                command.Type == GpuCanvasCommandType.SolidRect &&
                command.Color.R == byte.MaxValue && command.Color.G is >= 195 and <= 202 &&
                command.Color.B is >= 18 and <= 24 && command.Color.A > 0).ToArray();
            Require(pulseEdges.Length == 0,
                "A single ObjectField click still drew the removed yellow field pulse.");

            ResetObjectPingForTests();
            DragAndDrop.PrepareStartDrag();
            Dispatch(new Event(EventType.MouseDown)
                { mousePosition = new Vector2(210, 31), button = 0, clickCount = 1 },
                320, 64, DrawDifferentRects);
            Dispatch(new Event(EventType.MouseDrag)
                { mousePosition = new Vector2(222, 31), button = 0 }, 320, 64, DrawDifferentRects);
            Require(DragAndDrop.objectReferences is [var dragged] && ReferenceEquals(dragged, secondCandidate),
                "Dragging the second ObjectField published the first field's object.");
            var dragCommands = Render(320, 64, DrawDifferentRects);
            Require(!dragCommands.Any(command =>
                    command.Type == GpuCanvasCommandType.SolidRect &&
                    command.Color.R == byte.MaxValue && command.Color.G is >= 195 and <= 202 &&
                    command.Color.B is >= 18 and <= 24 && command.Color.A > 0),
                "Dragging an ObjectField drew the removed yellow field pulse.");

            void DrawDifferentRects()
            {
                first = EditorGUI.ObjectField(firstRect, "Target", first, typeof(GameObject), true);
                second = EditorGUI.ObjectField(secondRect, "Target", second, typeof(GameObject), true);
            }

            void DrawOverlappingRects()
            {
                using (new EditorGUI.DisabledScope(true))
                    first = EditorGUI.ObjectField(firstRect, "Target", first, typeof(GameObject), true);
                second = EditorGUI.ObjectField(firstRect, "Target", second, typeof(GameObject), true);
            }
        }
        finally
        {
            Selection.activeObject = null;
            DragAndDrop.PrepareStartDrag();
            handler.SetValue(null, null);
        }
    }

    private static void VerifyTabbedObjectPicker()
    {
        var assembly = typeof(EditorGUI).Assembly;
        var dispatcher = assembly.GetType("BEngine.Editor.EditorObjectPickerPopupDispatcher", true)!;
        var handler = dispatcher.GetProperty("Handler", BindingFlags.Static | BindingFlags.NonPublic)!;
        var invoke = handler.PropertyType.GetMethod("Invoke")!;
        var parameterType = invoke.GetParameters()[0].ParameterType;
        var parameter = Expression.Parameter(parameterType, "request");
        var capture = typeof(Program).GetMethod(nameof(CaptureObjectPickerRequest),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        handler.SetValue(null, Expression.Lambda(handler.PropertyType,
            Expression.Call(capture, Expression.Convert(parameter, typeof(object))), parameter).Compile());

        using var scene = new Scene("Tabbed Object Picker");
        var firstCandidate = scene.CreateGameObject("First Field");
        var secondCandidate = scene.CreateGameObject("Second Field");
        var firstRect = new Rect(0, 0, 320, 18);
        var secondRect = new Rect(0, 22, 320, 18);
        BObject? first = null;
        BObject? second = null;
        try
        {
            _capturedObjectPickerRequest = null;
            Dispatch(new Event(EventType.MouseDown)
                { mousePosition = new Vector2(310, 31), button = 0 }, 320, 64, DrawFields);
            Dispatch(new Event(EventType.MouseUp)
                { mousePosition = new Vector2(310, 31), button = 0 }, 320, 64, DrawFields);
            var request = _capturedObjectPickerRequest ??
                          throw new InvalidOperationException("Select Object did not open the tabbed picker.");
            Require((bool)(request.GetType().GetProperty("AllowSceneObjects")!.GetValue(request) ?? false),
                "Object picker request lost allowSceneObjects.");
            var select = request.GetType().GetProperty("Select")!.GetValue(request) as Action<BObject?>;
            Require(select is not null, "Object picker request has no field-scoped selection callback.");
            select!(secondCandidate);
            Dispatch(new Event(EventType.Repaint), 320, 64, DrawFields);
            Require(first is null && ReferenceEquals(second, secondCandidate),
                "A tabbed picker result opened by the second ObjectField leaked into the first field.");

            VerifyPickerTabs(assembly, firstCandidate, secondCandidate);
        }
        finally
        {
            handler.SetValue(null, null);
            _capturedObjectPickerRequest = null;
        }

        void DrawFields()
        {
            first = EditorGUI.ObjectField(firstRect, "Target", first, typeof(GameObject), true);
            second = EditorGUI.ObjectField(secondRect, "Target", second, typeof(GameObject), true);
        }
    }

    private static void VerifyPickerTabs(Assembly assembly, BObject sceneObject, BObject projectObject)
    {
        var candidateType = assembly.GetType("BEngine.Editor.EditorObjectPickerCandidate", true)!;
        var candidateConstructor = candidateType.GetConstructors(BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic).Single(constructor =>
            constructor.GetParameters().Length == 2);
        var sceneCandidate = candidateConstructor.Invoke([sceneObject, "Demo Scene/Root/Scene Object"]);
        var projectCandidate = candidateConstructor.Invoke([projectObject, "Assets/Prefabs/Project Object"]);
        var sceneCandidates = Array.CreateInstance(candidateType, 1);
        var projectCandidates = Array.CreateInstance(candidateType, 1);
        sceneCandidates.SetValue(sceneCandidate, 0);
        projectCandidates.SetValue(projectCandidate, 0);

        var requestType = assembly.GetType("BEngine.Editor.EditorObjectPickerRequest", true)!;
        var requestConstructor = requestType.GetConstructors(BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic).Single(constructor =>
            constructor.GetParameters().Length == 8);
        Action<BObject?> select = _ => { };
        var request = requestConstructor.Invoke([
            new Rect(0, 0, 320, 18), null, typeof(BObject), true, sceneObject,
            sceneCandidates, projectCandidates, select
        ]);
        var pickerType = assembly.GetType("BEngine.Editor.ImGuiObjectPicker", true)!;
        var picker = Activator.CreateInstance(pickerType, nonPublic: true)!;
        pickerType.GetMethod("Open", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(picker, [request, new Vector2(0, 20), null]);

        var tabs = ((IEnumerable)pickerType.GetProperty("tabs", BindingFlags.Instance |
            BindingFlags.NonPublic)!.GetValue(picker)!).Cast<string>().ToArray();
        Require(tabs.SequenceEqual(["Assets", "Scene"]),
            "Object picker does not expose separate Assets and Scene tabs.");
        Require((string?)pickerType.GetProperty("activeTab", BindingFlags.Instance |
                    BindingFlags.NonPublic)!.GetValue(picker) == "Assets",
            "Object picker did not open on the Assets tab.");
        var tree = pickerType.GetField("_assetsTree", BindingFlags.Instance |
            BindingFlags.NonPublic)!.GetValue(picker)!;
        var treeViewType = assembly.GetType("UnityEditor.IMGUI.Controls.TreeView", true)!;
        Require(treeViewType.IsInstanceOfType(tree),
            "Object picker content is not backed by UnityEditor.IMGUI.Controls.TreeView.");
        Require(tree.GetType().GetMethod("RowGUI", BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly) is null,
            "Object picker replaced the IMGUI TreeView's standard RowGUI/foldout rendering.");
        Require(EditorBuiltinIcons.Resolve("FoldoutClosed") == EditorBuiltinIcons.Toolbar.FoldoutClosed &&
                EditorBuiltinIcons.Resolve("FoldoutOpen") == EditorBuiltinIcons.Toolbar.FoldoutOpen,
            "IMGUI TreeView foldouts resolve to missing window-icon paths.");
        var visible = ((IEnumerable)pickerType.GetProperty("visiblePaths", BindingFlags.Instance |
            BindingFlags.NonPublic)!.GetValue(picker)!).Cast<string>().ToArray();
        Require(visible.Any(path => path.Equals("Prefabs", StringComparison.Ordinal)) &&
                !visible.Any(path => path.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                                    path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)),
            "Assets tab did not hide its virtual Assets root and expose direct children.");
        var assetsTree = (TreeView)tree;
        var prefabs = assetsTree.GetRows().Single(item =>
            item.displayName.Equals("Prefabs", StringComparison.Ordinal));
        Require(prefabs.depth == 0 && prefabs.hasChildren,
            "Assets virtual-root removal produced an invalid TreeView depth or lost folder children.");
        Require(assetsTree.SetExpanded(prefabs.id, true),
            "The standard IMGUI TreeView foldout could not expand an Assets folder.");
        visible = ((IEnumerable)pickerType.GetProperty("visiblePaths", BindingFlags.Instance |
            BindingFlags.NonPublic)!.GetValue(picker)!).Cast<string>().ToArray();
        Require(visible.Any(path => path.Equals("Prefabs/Project Object", StringComparison.Ordinal)),
            "Expanding the standard IMGUI TreeView foldout did not reveal the asset row.");
        assetsTree.SetExpanded(prefabs.id, false);

        pickerType.GetField("_search", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(picker, "Project Object");
        pickerType.GetMethod("Refilter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, null);
        visible = ((IEnumerable)pickerType.GetProperty("visiblePaths", BindingFlags.Instance |
            BindingFlags.NonPublic)!.GetValue(picker)!).Cast<string>().ToArray();
        Require(visible.Any(path => path.EndsWith("Project Object", StringComparison.Ordinal)),
            "Assets TreeView search did not flatten and find a matching resource.");
        pickerType.GetField("_search", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(picker, string.Empty);
        pickerType.GetMethod("Refilter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, null);

        var tabType = pickerType.GetNestedType("PickerTab", BindingFlags.NonPublic)!;
        var sceneTab = Enum.Parse(tabType, "Scene");
        pickerType.GetMethod("SwitchTab", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(picker, [sceneTab]);
        visible = ((IEnumerable)pickerType.GetProperty("visiblePaths", BindingFlags.Instance |
            BindingFlags.NonPublic)!.GetValue(picker)!).Cast<string>().ToArray();
        Require(visible.Any(path => path.Equals("Demo Scene", StringComparison.Ordinal)),
            "Scene tab did not display its scene hierarchy.");
        pickerType.GetField("_search", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(picker, "Scene Object");
        pickerType.GetMethod("Refilter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, null);
        visible = ((IEnumerable)pickerType.GetProperty("visiblePaths", BindingFlags.Instance |
            BindingFlags.NonPublic)!.GetValue(picker)!).Cast<string>().ToArray();
        Require(visible.Any(path => path.EndsWith("Scene Object", StringComparison.Ordinal)),
            "Scene TreeView search did not flatten and find a matching hierarchy item.");
        var draw = pickerType.GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Dispatch(new Event(EventType.MouseMove) { mousePosition = new Vector2(12, 28) }, 640, 480,
            () => draw.Invoke(picker, null));
        Dispatch(new Event(EventType.MouseMove) { mousePosition = new Vector2(630, 470) }, 640, 480,
            () => draw.Invoke(picker, null));
        Require(!(bool)(pickerType.GetProperty("isOpen", BindingFlags.Instance |
                    BindingFlags.NonPublic)!.GetValue(picker) ?? true),
            "Object picker stayed open after the pointer left its popup bounds.");
    }

    private static void OpenObjectMenu(Rect rect, BObject? value, Type type, bool allowSceneObjects)
    {
        _capturedMenuItems = null;
        _capturedMenuAdvanced = false;
        Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(310, 9), button = 0 }, 320, 40,
            () => EditorGUI.ObjectField(rect, "Target", value, type, allowSceneObjects));
        Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(310, 9), button = 0 }, 320, 40,
            () => EditorGUI.ObjectField(rect, "Target", value, type, allowSceneObjects));
        Require(_capturedMenuItems is not null, "Clicking ObjectField did not open its picker.");
    }

    private static void OpenSerializedObjectMenu(
        Rect rect, SerializedProperty property, Type type, bool allowSceneObjects)
    {
        _capturedMenuItems = null;
        _capturedMenuAdvanced = false;
        Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(310, 9), button = 0 }, 320, 40,
            () => EditorGUI.ObjectField(rect, property, type, allowSceneObjects));
        Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(310, 9), button = 0 }, 320, 40,
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

    private static void ResetObjectPingForTests() => typeof(EditorGUI).Assembly
        .GetType("BEngine.Editor.EditorObjectPing", true)!
        .GetMethod("ResetForTests", BindingFlags.Static | BindingFlags.NonPublic)!
        .Invoke(null, null);

    private static void CaptureObjectPickerRequest(object request) =>
        _capturedObjectPickerRequest = request;

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
