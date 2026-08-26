using System.Reflection;
using System.Linq.Expressions;
using BEngine.Editor;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.IMGUIStyleOverloads;

internal static class Program
{
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod("BeginFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod("EndFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly NullabilityInfoContext Nullability = new();
    private static int _objectPickerMenuCount;

    private static readonly IReadOnlyDictionary<Type, string[]> DrawingFamilies =
        new Dictionary<Type, string[]>
        {
            [typeof(GUI)] =
            [
                nameof(GUI.Label), nameof(GUI.Box), nameof(GUI.Button), nameof(GUI.Toggle),
                nameof(GUI.TextField), nameof(GUI.TextArea), nameof(GUI.PasswordField),
                nameof(GUI.HorizontalSlider), nameof(GUI.VerticalSlider), nameof(GUI.BeginScrollView)
            ],
            [typeof(GUILayout)] =
            [
                nameof(GUILayout.Label), nameof(GUILayout.Box), nameof(GUILayout.Button),
                nameof(GUILayout.Toggle), nameof(GUILayout.TextField), nameof(GUILayout.TextArea),
                nameof(GUILayout.PasswordField), nameof(GUILayout.HorizontalSlider),
                nameof(GUILayout.VerticalSlider), nameof(GUILayout.BeginHorizontal),
                nameof(GUILayout.BeginVertical), nameof(GUILayout.BeginArea), nameof(GUILayout.Area)
            ],
            [typeof(EditorGUI)] =
            [
                nameof(EditorGUI.PrefixLabel), nameof(EditorGUI.LabelField),
                nameof(EditorGUI.SelectableLabel), nameof(EditorGUI.Toggle), nameof(EditorGUI.TextField),
                nameof(EditorGUI.TextArea), nameof(EditorGUI.IntField), nameof(EditorGUI.FloatField),
                nameof(EditorGUI.ColorField), nameof(EditorGUI.ObjectField), nameof(EditorGUI.Popup),
                nameof(EditorGUI.AdvancedPopup), nameof(EditorGUI.DropDownButton),
                nameof(EditorGUI.DropdownButton), nameof(EditorGUI.EnumPopup),
                nameof(EditorGUI.Vector2Field), nameof(EditorGUI.Vector4Field), nameof(EditorGUI.Slider),
                nameof(EditorGUI.IntSlider), nameof(EditorGUI.Foldout), nameof(EditorGUI.HelpBox),
                nameof(EditorGUI.PropertyField), nameof(EditorGUI.DefaultPropertyField)
            ],
            [typeof(EditorGUILayout)] =
            [
                nameof(EditorGUILayout.PropertyField), nameof(EditorGUILayout.LabelField),
                nameof(EditorGUILayout.TextField), nameof(EditorGUILayout.TextArea),
                nameof(EditorGUILayout.IntField), nameof(EditorGUILayout.FloatField),
                nameof(EditorGUILayout.Toggle), nameof(EditorGUILayout.ColorField),
                nameof(EditorGUILayout.ObjectField), nameof(EditorGUILayout.Popup),
                nameof(EditorGUILayout.AdvancedPopup), nameof(EditorGUILayout.DropDownButton),
                nameof(EditorGUILayout.DropdownButton), nameof(EditorGUILayout.EnumPopup),
                nameof(EditorGUILayout.Slider), nameof(EditorGUILayout.IntSlider),
                nameof(EditorGUILayout.Vector2Field), nameof(EditorGUILayout.Vector4Field),
                nameof(EditorGUILayout.Foldout), nameof(EditorGUILayout.HelpBox)
            ],
            [typeof(EditorToolbar)] =
            [
                nameof(EditorToolbar.Button), nameof(EditorToolbar.Toggle),
                nameof(EditorToolbar.IconButton), nameof(EditorToolbar.IconToggle),
                nameof(EditorToolbar.SearchField)
            ]
        };

    private static int Main()
    {
        var previousSkin = GUI.skin;
        try
        {
            VerifyNullableStyleSurface();
            VerifyCurrentSkinFallback();
            VerifyExplicitStylesAndStateRendering();
            VerifyTextFieldCaretStyle();
            VerifyObjectFieldSelectionAndPickerSplit();
            VerifyLayoutNullEquivalence();
            VerifyLayoutAndToolbarNullStyles();
            Console.WriteLine("IMGUI_STYLE_OVERLOADS_OK|reflection-nullability,skin-fallback," +
                              "toggle-states,slider-states,scrollbar-states,selectable-label," +
                              "textfield-caret,object-field-split,layout-null-equivalence,layout-containers,toolbar");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            GUI.skin = previousSkin;
            GUI.enabled = true;
            GUI.color = Color.white;
            GUI.backgroundColor = Color.white;
            GUI.contentColor = Color.white;
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
        }
    }

    private static void VerifyNullableStyleSurface()
    {
        foreach (var (type, families) in DrawingFamilies)
        foreach (var family in families)
        {
            var styleParameters = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == family)
                .SelectMany(method => method.GetParameters())
                .Where(parameter => parameter.ParameterType == typeof(GUIStyle))
                .ToArray();
            Require(styleParameters.Length > 0,
                $"{type.Name}.{family} has no public GUIStyle overload.");
            Require(styleParameters.Any(parameter =>
                    Nullability.Create(parameter).ReadState == NullabilityState.Nullable),
                $"{type.Name}.{family} does not declare a nullable GUIStyle parameter.");
        }

        RequireNullableStyleSignature(typeof(GUI), nameof(GUI.TextField),
            typeof(Rect), typeof(string), typeof(GUIStyle));
        RequireNullableStyleSignature(typeof(GUI), nameof(GUI.TextArea),
            typeof(Rect), typeof(string), typeof(GUIStyle));
        RequireNullableStyleSignature(typeof(GUI), nameof(GUI.PasswordField),
            typeof(Rect), typeof(string), typeof(GUIStyle));
        RequireNullableStyleSignature(typeof(GUI), nameof(GUI.PasswordField),
            typeof(Rect), typeof(string), typeof(char), typeof(GUIStyle));
        RequireNullableStyleSignature(typeof(GUI), nameof(GUI.HorizontalSlider),
            typeof(Rect), typeof(Fix64), typeof(Fix64), typeof(Fix64), typeof(GUIStyle));
        RequireNullableStyleSignature(typeof(GUI), nameof(GUI.VerticalSlider),
            typeof(Rect), typeof(Fix64), typeof(Fix64), typeof(Fix64), typeof(GUIStyle));
    }

    private static void VerifyCurrentSkinFallback()
    {
        var skin = new GUISkin();
        skin.label.normal.textColor = Color.green;
        skin.button.normal.backgroundColor = new Color(1, 0, 1, 1);
        GUI.skin = skin;
        GUIStyle? style = null;

        var label = Render(() => GUI.Label(new Rect(10, 8, 180, 24), "Skin Label", style));
        Require(HasText(label, "Skin Label", Color.green),
            "GUI.Label(null) did not resolve GUI.skin.label.");

        var layout = Render(() => GUILayout.Button("Skin Button", style), height: 100);
        Require(HasRect(layout, skin.button.normal.backgroundColor),
            "GUILayout.Button(null) did not resolve GUI.skin.button.");
    }

    private static void VerifyExplicitStylesAndStateRendering()
    {
        var skin = new GUISkin();
        skin.toggle.normal.backgroundColor = Color.blue;
        skin.toggle.onNormal.backgroundColor = Color.red;
        skin.toggle.onNormal.textColor = Color.white;
        skin.horizontalSlider.normal.backgroundColor = Color.blue;
        skin.horizontalSliderThumb.normal.backgroundColor = Color.green;
        skin.verticalScrollbar.normal.backgroundColor = Color.blue;
        skin.verticalScrollbarThumb.normal.backgroundColor = Color.red;
        skin.horizontalScrollbar.normal.backgroundColor = Color.green;
        skin.horizontalScrollbarThumb.normal.backgroundColor = Color.white;
        skin.scrollView.normal.backgroundColor = Color.black;
        GUI.skin = skin;
        GUIStyle? style = null;

        var selectableStyle = new GUIStyle("selectable");
        selectableStyle.normal.textColor = Color.blue;
        var selectable = Render(() => EditorGUI.SelectableLabel(
            new Rect(10, 8, 180, 24), "Selectable", selectableStyle));
        Require(HasText(selectable, "Selectable", Color.blue),
            "EditorGUI.SelectableLabel ignored its GUIStyle.");

        var toggle = Render(() => GUI.Toggle(new Rect(10, 8, 180, 24), true, "Enabled", style));
        Require(HasRect(toggle, skin.toggle.onNormal.backgroundColor),
            "GUI.Toggle did not render the active onNormal style state.");

        var slider = Render(() => GUI.HorizontalSlider(
            new Rect(10, 8, 180, 24), 5, 0, 10, style));
        Require(HasRect(slider, skin.horizontalSlider.normal.backgroundColor) &&
                HasRect(slider, skin.horizontalSliderThumb.normal.backgroundColor),
            "GUI.HorizontalSlider did not render the skin track and thumb states.");

        var scrollbars = Render(() =>
        {
            GUI.BeginScrollView(new Rect(10, 8, 100, 80), Vector2.zero,
                new Rect(0, 0, 260, 220), style, style, style);
            GUI.EndScrollView();
        }, width: 180, height: 130);
        Require(HasRect(scrollbars, skin.scrollView.normal.backgroundColor) &&
                HasRect(scrollbars, skin.verticalScrollbar.normal.backgroundColor) &&
                HasRect(scrollbars, skin.verticalScrollbarThumb.normal.backgroundColor) &&
                HasRect(scrollbars, skin.horizontalScrollbar.normal.backgroundColor) &&
                HasRect(scrollbars, skin.horizontalScrollbarThumb.normal.backgroundColor),
            "GUI.BeginScrollView(null styles) did not render current-skin scrollbar states.");
    }

    private static void VerifyLayoutAndToolbarNullStyles()
    {
        var skin = new GUISkin();
        skin.textField.normal.backgroundColor = Color.blue;
        skin.toolbarIconButton.normal.backgroundColor = Color.red;
        skin.toolbarIconButton.hover.backgroundColor = Color.red;
        skin.toolbarIconButton.active.backgroundColor = Color.red;
        skin.toolbarSearchField.normal.backgroundColor = Color.green;
        skin.toolbarSearchField.hover.backgroundColor = Color.green;
        skin.toolbarSearchField.active.backgroundColor = Color.green;
        skin.toolbarSearchField.focused.backgroundColor = Color.green;
        GUI.skin = skin;
        GUIStyle? style = null;
        Require(ReferenceEquals(EditorStyles.toolbarSearchField, skin.toolbarSearchField),
            "EditorStyles.toolbarSearchField is not dynamic to the active GUISkin.");

        var editorLayout = Render(() => EditorGUILayout.TextField("Name", "Value", style), height: 100);
        Require(HasRect(editorLayout, skin.textField.normal.backgroundColor),
            "EditorGUILayout.TextField(null) did not resolve the active skin textField.");

        var toolbar = Render(() => EditorToolbar.Button(
            new Rect(10, 8, 80, 24), new GUIContent("Run"), style), width: 260, height: 100);
        Require(HasRect(toolbar, skin.toolbarIconButton.normal.backgroundColor),
            "EditorToolbar.Button(null) did not resolve the active toolbarIconButton skin slot.");
        var explicitSearch = Render(() => EditorToolbar.SearchField(
            "query", skin.toolbarSearchField, GUILayout.Width(160)), width: 260, height: 100);
        Require(HasRect(explicitSearch, skin.toolbarSearchField.normal.backgroundColor),
            "EditorToolbar.SearchField ignored an explicit toolbarSearchField style.");
        Require(ReferenceEquals(GUI.skin, skin) &&
                ReferenceEquals(EditorStyles.toolbarSearchField, skin.toolbarSearchField) &&
                skin.toolbarSearchField.normal.backgroundColor.Equals(Color.green),
            "Explicit toolbar search rendering mutated the active GUISkin or its search style.");
        var fallbackSearch = Render(() => EditorToolbar.SearchField(
            "query", style, GUILayout.Width(160)), width: 260, height: 100);
        Require(HasRect(fallbackSearch, skin.toolbarSearchField.normal.backgroundColor),
            "EditorToolbar.SearchField(null) did not resolve the active toolbarSearchField skin slot. " +
            $"Solid colors: {string.Join(';', fallbackSearch.Where(command =>
                command.Type == GpuCanvasCommandType.SolidRect).Select(command =>
                $"{command.Color}@{command.Rect}"))}");
    }

    private static void VerifyLayoutNullEquivalence()
    {
        var skin = new GUISkin();
        skin.button.fixedHeight = 41;
        skin.box.normal.backgroundColor = Color.blue;
        GUI.skin = skin;
        GUIStyle? style = null;
        var ordinary = default(Rect);
        var explicitNull = default(Rect);

        var commands = Render(() =>
        {
            GUILayout.Button("Ordinary");
            ordinary = GUILayoutUtility.GetLastRect();
            GUILayout.Button("Explicit Null", style);
            explicitNull = GUILayoutUtility.GetLastRect();
            GUILayout.BeginHorizontal(style, GUILayout.Height(31));
            GUILayout.EndHorizontal();
        }, height: 180);

        Require(ordinary.height == 41 && explicitNull.height == 41 && ordinary.height == explicitNull.height,
            "GUILayout.Button() and GUILayout.Button(null) do not share skin fixedHeight semantics.");
        Require(HasRect(commands, skin.box.normal.backgroundColor),
            "Styled GUILayout containers did not draw their current-skin background.");
    }

    private static void VerifyTextFieldCaretStyle()
    {
        GUI.skin = new GUISkin();
        var style = new GUIStyle("caret-style");
        style.normal.backgroundColor = Color.black;
        style.focused.backgroundColor = Color.black;
        style.focused.textColor = Color.red;
        var field = new Rect(10, 8, 180, 24);
        GUIUtility.keyboardControl = 0;

        Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(40, 16), button = 0 },
            () => GUI.TextField(field, "Caret", style));
        var commands = Render(() => GUI.TextField(field, "Caret", style));
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                        command.Color == GpuCanvasColor.FromColor(Color.red) &&
                                        Math.Abs(command.Rect.Width - 1) < .01f && command.Rect.Height > 10),
            "GUI.TextField caret ignored the focused GUIStyle text color.");
        GUIUtility.keyboardControl = 0;
    }

    private static void VerifyObjectFieldSelectionAndPickerSplit()
    {
        var dispatcher = typeof(GUI).Assembly.GetType("BEngine.Editor.GenericMenuDispatcher", true)!;
        var handler = dispatcher.GetProperty("Handler", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousHandler = handler.GetValue(null);
        var value = new GUISkin { name = "Object Field Skin" };
        var field = new Rect(10, 8, 180, 24);
        try
        {
            _objectPickerMenuCount = 0;
            Selection.activeObject = null;
            handler.SetValue(null, BuildMenuCapture(handler.PropertyType));
            Click(new Vector2(40, 16), () => EditorGUI.ObjectField(
                field, value, typeof(GUISkin), allowSceneObjects: false));
            Require(ReferenceEquals(Selection.activeObject, value) && _objectPickerMenuCount == 0,
                "Clicking an ObjectField body did not select its value or incorrectly opened the picker.");

            Click(new Vector2(184, 16), () => EditorGUI.ObjectField(
                field, value, typeof(GUISkin), allowSceneObjects: false));
            Require(_objectPickerMenuCount == 1,
                "Clicking an ObjectField picker button did not open exactly one object menu.");
        }
        finally
        {
            Selection.activeObject = null;
            handler.SetValue(null, previousHandler);
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
        }
    }

    private static Delegate BuildMenuCapture(Type delegateType)
    {
        var parameter = Expression.Parameter(delegateType.GetMethod("Invoke")!.GetParameters()[0].ParameterType,
            "items");
        var capture = typeof(Program).GetMethod(nameof(CaptureMenu),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return Expression.Lambda(delegateType,
            Expression.Call(capture, Expression.Convert(parameter, typeof(object))), parameter).Compile();
    }

    private static void CaptureMenu(object _) => _objectPickerMenuCount++;

    private static void Click(Vector2 point, Action draw)
    {
        Dispatch(new Event(EventType.MouseDown) { mousePosition = point, button = 0 }, draw);
        Dispatch(new Event(EventType.MouseUp) { mousePosition = point, button = 0 }, draw);
    }

    private static List<GpuCanvasCommand> Render(Action draw, int width = 240, int height = 80)
    {
        var commands = new List<GpuCanvasCommand>();
        Dispatch(new Event(EventType.Repaint), draw, commands, width, height);
        return commands;
    }

    private static void Dispatch(Event evt, Action draw, List<GpuCanvasCommand>? commands = null,
        int width = 240, int height = 80)
    {
        BeginFrame.Invoke(null, [evt, width, height, commands ?? []]);
        try { draw(); }
        finally { EndFrame.Invoke(null, null); }
    }

    private static bool HasRect(IEnumerable<GpuCanvasCommand> commands, Color color) =>
        commands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                command.Color == GpuCanvasColor.FromColor(color));

    private static bool HasText(IEnumerable<GpuCanvasCommand> commands, string text, Color color) =>
        commands.Any(command => command.Type == GpuCanvasCommandType.Text && command.Content == text &&
                                command.Color == GpuCanvasColor.FromColor(color));

    private static void RequireNullableStyleSignature(Type type, string name, params Type[] parameterTypes)
    {
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static,
            binder: null, parameterTypes, modifiers: null);
        Require(method is not null,
            $"{type.Name}.{name}({string.Join(", ", parameterTypes.Select(item => item.Name))}) is missing.");
        var style = method!.GetParameters().Single(parameter => parameter.ParameterType == typeof(GUIStyle));
        Require(Nullability.Create(style).ReadState == NullabilityState.Nullable,
            $"{type.Name}.{name}'s direct GUIStyle overload is not nullable.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
