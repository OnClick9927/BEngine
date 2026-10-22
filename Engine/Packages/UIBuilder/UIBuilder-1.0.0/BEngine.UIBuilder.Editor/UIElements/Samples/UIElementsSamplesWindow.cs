using BEngine.Editor;

namespace BEngine.UIElements.Editor;

[EditorWindowIcon("Icons/Windows/UIBuilder.png")]
public sealed class UIElementsSamplesWindow :
    EditorWindow<UIElementsSamplesWindowModel, UIElementsSamplesWindowController>
{
    private static readonly string[] Categories =
    [
        "Styles", "Button", "Scroller", "Toggle", "RadioButton", "Label", "Text Field",
        "HelpBox", "Object Field", "List View", "Numeric Fields", "Value Fields", "Choice Fields"
    ];

    private static readonly string[] Choices = ["Low", "Medium", "High", "Ultra"];
    private static readonly string[] ListItems =
    [
        "VisualElement", "Label", "Button", "TextField", "Toggle", "Slider", "DropdownField",
        "ListView", "TreeView", "ScrollView"
    ];

    public UIElementsSamplesWindow()
    {
        titleContent = new GUIContent("UIElements Samples", "Icons/Windows/UIBuilder.png");
        minSize = new Vector2(720, 480);
        position = new Rect(110, 90, 1040, 700);
    }

    protected override UIElementsSamplesWindowModel CreateWindowModel(EditorWindowMvcContext context) =>
        new(context.WindowId);

    protected override UIElementsSamplesWindowController CreateWindowController(
        EditorWindowMvcContext context,
        UIElementsSamplesWindowModel windowModel) => new(context, windowModel);

    [MenuItem("Window/UIElement/Samples", false, 233)]
    public static void Open() => GetWindow<UIElementsSamplesWindow>("UIElements Samples", true).ShowAuxWindow();

    protected override void OnGUI()
    {
        var width = Fix64.Max(1, GUIUtility.currentViewWidth);
        var height = Fix64.Max(1, GUIUtility.currentViewHeight);
        var navigationWidth = Fix64.Clamp(width * Fix64.FromDecimal(0.32m), 220, 360);
        DrawNavigation(new Rect(0, 0, navigationWidth, height));
        GUI.DrawRect(new Rect(navigationWidth, 0, 1, height),
            EditorStyles.separator.normal.backgroundColor);
        DrawSelectedSample(new Rect(navigationWidth + 1, 0,
            Fix64.Max(1, width - navigationWidth - 1), height));
    }

    private void DrawNavigation(Rect area)
    {
        GUI.Box(area, string.Empty, EditorStyles.viewBackground);
        GUI.Box(new Rect(area.x, area.y, area.width, 30), string.Empty, EditorStyles.toolbar);
        GUI.Label(new Rect(area.x + 8, area.y, area.width - 16, 30),
            "UIElements Samples", EditorStyles.boldLabel);
        for (var index = 0; index < Categories.Length; index++)
        {
            var style = index == model.SelectedCategory
                ? EditorStyles.treeViewRowSelected
                : EditorStyles.treeViewRow;
            if (GUI.Button(new Rect(area.x + 6, area.y + 34 + index * 25,
                    Fix64.Max(1, area.width - 12), 25), Categories[index], style))
                controller.SelectCategory(index);
        }
    }

    private void DrawSelectedSample(Rect area)
    {
        GUI.Box(area, string.Empty, EditorStyles.viewBackground);
        GUI.Box(new Rect(area.x, area.y, area.width, 36), string.Empty, EditorStyles.toolbar);
        GUI.Label(new Rect(area.x + 12, area.y, Fix64.Max(1, area.width - 24), 36),
            Categories[model.SelectedCategory], EditorStyles.largeLabel);
        var viewport = new Rect(area.x, area.y + 36, area.width, Fix64.Max(1, area.height - 36));
        var contentHeight = model.SelectedCategory == 0 ? 940 : 620;
        model.ContentScroll = GUI.BeginScrollView(viewport, model.ContentScroll,
            new Rect(0, 0, Fix64.Max(1, viewport.width - 14), contentHeight));
        try
        {
            GUILayout.BeginArea(new Rect(12, 8, Fix64.Max(1, viewport.width - 38), contentHeight - 16));
            try
            {
                switch (model.SelectedCategory)
                {
                    case 0: DrawStyles(); break;
                    case 1: DrawButtons(); break;
                    case 2: DrawScroller(); break;
                    case 3: DrawToggles(); break;
                    case 4: DrawRadioButtons(); break;
                    case 5: DrawLabels(); break;
                    case 6: DrawTextFields(); break;
                    case 7: DrawHelpBoxes(); break;
                    case 8: DrawObjectFields(); break;
                    case 9: DrawListView(); break;
                    case 10: DrawNumericFields(); break;
                    case 11: DrawValueFields(); break;
                    case 12: DrawChoiceFields(); break;
                }
            }
            finally { GUILayout.EndArea(); }
        }
        finally { GUI.EndScrollView(); }
    }

    private void DrawStyles()
    {
        Section("Button");
        DrawButtonStateRow();
        GUILayout.Space(10);
        Section("Toggle");
        DrawToggleRows();
        GUILayout.Space(10);
        Section("Slider");
        model.SliderValue = EditorGUILayout.Slider("Normal", model.SliderValue, 0, 1);
        WithDisabled(() => _ = EditorGUILayout.Slider("Disabled", model.SliderValue, 0, 1));
        _ = EditorGUILayout.Slider("Hover", model.SliderValue, 0, 1);
        _ = EditorGUILayout.Slider("Active, Hover", model.SliderValue, 0, 1);
        _ = EditorGUILayout.Slider("Active, Focus", model.SliderValue, 0, 1);
        GUILayout.Space(10);
        Section("Text Field");
        model.TextValue = EditorGUILayout.TextField("Normal", model.TextValue);
        WithDisabled(() => _ = EditorGUILayout.TextField("Disabled", model.TextValue));
        _ = EditorGUILayout.TextField("Hover", model.TextValue);
        _ = EditorGUILayout.TextField("Active, Hover", model.TextValue);
        _ = EditorGUILayout.TextField("Active, Focus", model.TextValue);
        GUILayout.Space(10);
        Section("Help Box");
        EditorGUILayout.HelpBox("Informational message", MessageType.Info);
        EditorGUILayout.HelpBox("A warning state", MessageType.Warning);
        EditorGUILayout.HelpBox("An error state", MessageType.Error);
        GUILayout.Space(10);
        Section("Choice Field");
        model.ChoiceValue = EditorGUILayout.Popup("Quality", model.ChoiceValue, Choices);
        GUILayout.Space(10);
        Section("Value Fields");
        model.IntValue = EditorGUILayout.IntField("Integer", model.IntValue);
        model.FloatValue = EditorGUILayout.FloatField("Float", model.FloatValue);
        model.ColorValue = EditorGUILayout.ColorField("Color", model.ColorValue);
    }

    private void DrawButtons()
    {
        Section("Button States");
        DrawButtonStateRow();
        GUILayout.Space(12);
        Section("Button Groups");
        GUILayout.BeginHorizontal(GUILayout.Height(24));
        if (GUILayout.Button("Left", EditorStyles.miniButtonLeft)) model.ButtonClicks++;
        if (GUILayout.Button("Middle", EditorStyles.miniButtonMid)) model.ButtonClicks++;
        if (GUILayout.Button("Right", EditorStyles.miniButtonRight)) model.ButtonClicks++;
        GUILayout.EndHorizontal();
        GUILayout.Space(8);
        GUILayout.Label($"Invocations: {model.ButtonClicks}", EditorStyles.miniLabel);
    }

    private void DrawButtonStateRow()
    {
        GUILayout.BeginHorizontal(GUILayout.Height(25));
        if (GUILayout.Button("Normal", GUILayout.Height(25))) model.ButtonClicks++;
        if (GUILayout.Button("Checked", EditorStyles.toolbarIconButtonSelected, GUILayout.Height(25)))
            model.ButtonClicks++;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(GUILayout.Height(25));
        WithDisabled(() => _ = GUILayout.Button("Disabled", GUILayout.Height(25)));
        WithDisabled(() => _ = GUILayout.Button("Checked, Disabled",
            EditorStyles.toolbarIconButtonSelected, GUILayout.Height(25)));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(GUILayout.Height(25));
        if (GUILayout.Button("Hover", GUILayout.Height(25))) model.ButtonClicks++;
        if (GUILayout.Button("Hover, Checked", EditorStyles.toolbarIconButtonSelected, GUILayout.Height(25)))
            model.ButtonClicks++;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(GUILayout.Height(25));
        if (GUILayout.Button("Active, Hover", EditorStyles.toolbarIconButtonSelected, GUILayout.Height(25)))
            model.ButtonClicks++;
        if (GUILayout.Button("Active, Hover, Checked", EditorStyles.toolbarIconButtonSelected,
                GUILayout.Height(25)))
            model.ButtonClicks++;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(GUILayout.Height(25));
        if (GUILayout.Button("Active, Focus", EditorStyles.toolbarIconButtonSelected, GUILayout.Height(25)))
            model.ButtonClicks++;
        if (GUILayout.Button("Active, Checked, Focus", EditorStyles.toolbarIconButtonSelected,
                GUILayout.Height(25)))
            model.ButtonClicks++;
        GUILayout.EndHorizontal();
    }

    private void DrawScroller()
    {
        Section("Horizontal Scroller");
        model.SliderValue = EditorGUILayout.Slider("Position", model.SliderValue, 0, 1);
        GUILayout.Space(12);
        Section("Scrollable Surface");
        EditorGUILayout.HelpBox("Use the scrollbar on this sample pane to inspect content beyond the viewport.",
            MessageType.Info);
        for (var index = 1; index <= 14; index++)
            GUILayout.Label($"Scrollable row {index:00}", index % 2 == 0
                ? EditorStyles.treeViewRow : EditorStyles.label, GUILayout.Height(24));
    }

    private void DrawToggles()
    {
        Section("Toggle States");
        DrawToggleRows();
    }

    private void DrawToggleRows()
    {
        DrawToggleStateRow("Normal", model.ToggleValue, "Checked", true,
            value => model.ToggleValue = value);
        WithDisabled(() => DrawToggleStateRow("Disabled", false, "Checked, Disabled", true));
        DrawToggleStateRow("Hover", false, "Hover, Checked", true);
        DrawToggleStateRow("Active, Hover", true, "Active, Hover, Checked", true);
        DrawToggleStateRow("Active, Focus", true, "Active, Checked, Focus", true);
    }

    private static void DrawToggleStateRow(
        string leftLabel,
        bool leftValue,
        string rightLabel,
        bool rightValue,
        Action<bool>? applyLeft = null)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(22));
        var next = EditorGUILayout.Toggle(leftLabel, leftValue, GUILayout.Width(220));
        _ = EditorGUILayout.Toggle(rightLabel, rightValue, GUILayout.Width(220));
        GUILayout.EndHorizontal();
        applyLeft?.Invoke(next);
    }

    private void DrawRadioButtons()
    {
        Section("Radio Button Group");
        for (var index = 0; index < Choices.Length; index++)
        {
            if (GUILayout.Toggle(model.RadioValue == index, Choices[index], EditorStyles.radioButton))
                model.RadioValue = index;
        }
        GUILayout.Space(8);
        WithDisabled(() => _ = GUILayout.Toggle(false, "Disabled", EditorStyles.radioButton));
    }

    private void DrawLabels()
    {
        Section("Label Styles");
        GUILayout.Label("Standard label");
        GUILayout.Label("Bold label", EditorStyles.boldLabel);
        GUILayout.Label("Large label", EditorStyles.largeLabel);
        GUILayout.Label("Mini label", EditorStyles.miniLabel);
        GUILayout.Label("Centered label", EditorStyles.centeredBoldLabel);
        GUILayout.Space(12);
        Section("Wrapped Label");
        GUILayout.Label("Labels can wrap longer content while keeping the surrounding inspector layout stable.",
            EditorStyles.wordWrappedLabel, GUILayout.Height(44));
    }

    private void DrawTextFields()
    {
        Section("Single Line");
        model.TextValue = EditorGUILayout.TextField("Text", model.TextValue);
        WithDisabled(() => _ = EditorGUILayout.TextField("Disabled", model.TextValue));
        GUILayout.Space(12);
        Section("Multiline");
        model.MultilineValue = EditorGUILayout.TextArea(model.MultilineValue, GUILayout.Height(120));
    }

    private static void DrawHelpBoxes()
    {
        Section("Message Types");
        EditorGUILayout.HelpBox("This is an informational help box.", MessageType.Info);
        EditorGUILayout.HelpBox("This is a warning help box.", MessageType.Warning);
        EditorGUILayout.HelpBox("This is an error help box.", MessageType.Error);
        EditorGUILayout.HelpBox("This is a neutral help box.", MessageType.None);
    }

    private void DrawObjectFields()
    {
        Section("Object References");
        model.ObjectValue = EditorGUILayout.ObjectField("Object", model.ObjectValue, typeof(BObject), true);
        WithDisabled(() => _ = EditorGUILayout.ObjectField("Disabled", model.ObjectValue,
            typeof(BObject), true));
        GUILayout.Space(12);
        EditorGUILayout.HelpBox("Scene and asset objects can be assigned by picker or drag and drop.",
            MessageType.Info);
    }

    private void DrawListView()
    {
        Section("List View");
        for (var index = 0; index < ListItems.Length; index++)
        {
            var style = index == model.ListSelection
                ? EditorStyles.treeViewRowSelected
                : EditorStyles.treeViewRow;
            if (GUILayout.Button(ListItems[index], style, GUILayout.Height(24))) model.ListSelection = index;
        }
        GUILayout.Space(8);
        GUILayout.Label($"Selected: {ListItems[model.ListSelection]}", EditorStyles.miniLabel);
    }

    private void DrawNumericFields()
    {
        Section("Integer");
        model.IntValue = EditorGUILayout.IntField("Value", model.IntValue);
        _ = EditorGUILayout.IntSlider("Range", model.IntValue, 0, 100);
        GUILayout.Space(12);
        Section("Float");
        model.FloatValue = EditorGUILayout.FloatField("Value", model.FloatValue);
        model.SliderValue = EditorGUILayout.Slider("Normalized", model.SliderValue, 0, 1);
        GUILayout.Space(12);
        WithDisabled(() => _ = EditorGUILayout.FloatField("Disabled", model.FloatValue));
    }

    private void DrawValueFields()
    {
        Section("Vectors");
        model.Vector2Value = EditorGUILayout.Vector2Field("Vector 2", model.Vector2Value);
        model.Vector4Value = EditorGUILayout.Vector4Field("Vector 4", model.Vector4Value);
        GUILayout.Space(12);
        Section("Color");
        model.ColorValue = EditorGUILayout.ColorField("RGBA", model.ColorValue, true, true, false);
        _ = EditorGUILayout.ColorField("HDR", model.ColorValue, true, true, true);
    }

    private void DrawChoiceFields()
    {
        Section("Dropdown Field");
        model.ChoiceValue = EditorGUILayout.Popup("Quality", model.ChoiceValue, Choices);
        GUILayout.Space(10);
        Section("Enum");
        model.ChoiceValue = (int)(SampleMode)EditorGUILayout.EnumPopup("Mode", (SampleMode)model.ChoiceValue);
        GUILayout.Space(10);
        Section("Layer Mask");
        _ = EditorGUILayout.LayerMaskField("Layers", (ulong)model.IntValue);
        GUILayout.Space(10);
        Section("Popup Button");
        _ = EditorGUILayout.DropDownButton("Open options", FocusType.Keyboard);
    }

    private static void Section(string title)
    {
        GUILayout.Label(title, EditorStyles.boldLabel);
    }

    private static void WithDisabled(Action draw)
    {
        var previous = GUI.enabled;
        GUI.enabled = false;
        try { draw(); }
        finally { GUI.enabled = previous; }
    }

    private enum SampleMode
    {
        Low,
        Medium,
        High,
        Ultra
    }
}
