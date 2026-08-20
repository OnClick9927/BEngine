namespace BEngine.Editor;

public static class EditorStyles
{
    public static GUIStyle label { get; } = new("label");
    public static GUIStyle vectorAxisLabel { get; } = new("vectorAxisLabel");
    public static GUIStyle boldLabel { get; } = new("boldLabel");
    public static GUIStyle miniLabel { get; } = new("miniLabel") { fontSize = 11 };
    public static GUIStyle largeLabel { get; } = new("largeLabel") { fontSize = 16 };
    public static GUIStyle textField { get; } = new("textField");
    public static GUIStyle textArea { get; } = new("textArea") { wordWrap = true };
    public static GUIStyle numberField { get; } = new("numberField");
    public static GUIStyle popup { get; } = new("popup");
    public static GUIStyle colorField { get; } = new("colorField");
    public static GUIStyle colorPickerSwatch { get; } = new("colorPickerSwatch");
    public static GUIStyle helpBox { get; } = new("helpBox") { wordWrap = true, fixedHeight = 38 };
    public static GUIStyle toolbar { get; } = new("toolbar") { fixedHeight = 24 };
    public static GUIStyle toolbarButton { get; } = new("toolbarButton") { fixedHeight = 22 };
    public static GUIStyle toolbarIconButton { get; } = new("toolbarIconButton")
    {
        fixedWidth = 24,
        fixedHeight = 22,
        alignment = TextAnchor.MiddleCenter
    };
    public static GUIStyle toolbarIconButtonSelected { get; } = new("toolbarIconButtonSelected")
    {
        fixedWidth = 24,
        fixedHeight = 22,
        alignment = TextAnchor.MiddleCenter
    };
    public static GUIStyle toolbarSearchField { get; } = new("toolbarSearchField") { fixedHeight = 20 };
    public static GUIStyle dockTab { get; } = new("dockTab") { fixedHeight = 24 };
    public static GUIStyle dockTabActive { get; } = new("dockTabActive") { fixedHeight = 24 };
    public static GUIStyle windowTitle { get; } = new("windowTitle") { fixedHeight = 24 };
    public static GUIStyle inspectorTitlebar { get; } = new("inspectorTitlebar") { fixedHeight = 22 };
    public static GUIStyle menuItem { get; } = new("menuItem") { fixedHeight = 22 };
    public static GUIStyle menuItemDisabled { get; } = new("menuItemDisabled") { fixedHeight = 22 };
    public static GUIStyle miniButton { get; } = new("miniButton") { fixedHeight = 20 };
    public static GUIStyle treeViewRow { get; } = new("treeViewRow") { fixedHeight = 22 };
    public static GUIStyle treeViewRowSelected { get; } = new("treeViewRowSelected") { fixedHeight = 22 };
    public static GUIStyle hierarchySceneHeader { get; } = new("hierarchySceneHeader") { fixedHeight = 22 };
    public static GUIStyle hierarchySceneHeaderActive { get; } = new("hierarchySceneHeaderActive")
        { fixedHeight = 22 };
    public static GUIStyle hierarchyRow { get; } = new("hierarchyRow") { fixedHeight = 22 };
    public static GUIStyle hierarchyRowSelected { get; } = new("hierarchyRowSelected") { fixedHeight = 22 };
    public static GUIStyle hierarchyRowInactive { get; } = new("hierarchyRowInactive") { fixedHeight = 22 };
    public static GUIStyle hierarchyAction { get; } = new("hierarchyAction")
    {
        fixedWidth = 20,
        fixedHeight = 22,
        alignment = TextAnchor.MiddleCenter
    };
    public static GUIStyle statusBar { get; } = new("statusBar") { fixedHeight = 20 };
    public static GUIStyle foldout { get; } = new("foldout");
    public static GUIStyle linkLabel { get; } = new("linkLabel");
    public static GUIStyle inspectorDefaultMargins { get; } = new("inspectorDefaultMargins");
    public static GUIStyle separator { get; } = new("separator") { fixedHeight = 1 };

    internal static void ApplyAppearance(EditorThemePalette palette, int fontSize)
    {
        var lineHeight = Fix64.Max(18,
            GUITextMetrics.MeasureLineHeight(fontSize, GUIUtility.fontFamily));
        toolbar.fixedHeight = lineHeight + 4;
        toolbarButton.fixedHeight = lineHeight + 2;
        toolbarIconButton.fixedHeight = lineHeight + 2;
        toolbarIconButtonSelected.fixedHeight = lineHeight + 2;
        toolbarSearchField.fixedHeight = lineHeight;
        dockTab.fixedHeight = lineHeight + 2;
        dockTabActive.fixedHeight = lineHeight + 2;
        windowTitle.fixedHeight = lineHeight + 2;
        inspectorTitlebar.fixedHeight = lineHeight + 2;
        menuItem.fixedHeight = lineHeight + 2;
        menuItemDisabled.fixedHeight = lineHeight + 2;
        miniButton.fixedHeight = lineHeight;
        treeViewRow.fixedHeight = lineHeight;
        treeViewRowSelected.fixedHeight = lineHeight;
        hierarchySceneHeader.fixedHeight = lineHeight;
        hierarchySceneHeaderActive.fixedHeight = lineHeight;
        hierarchyRow.fixedHeight = lineHeight;
        hierarchyRowSelected.fixedHeight = lineHeight;
        hierarchyRowInactive.fixedHeight = lineHeight;
        hierarchyAction.fixedHeight = lineHeight;
        statusBar.fixedHeight = lineHeight;
        toolbarButton.alignment = TextAnchor.MiddleCenter;
        toolbarIconButton.alignment = TextAnchor.MiddleCenter;
        toolbarIconButtonSelected.alignment = TextAnchor.MiddleCenter;
        miniButton.alignment = TextAnchor.MiddleCenter;
        hierarchyAction.alignment = TextAnchor.MiddleCenter;
        popup.alignment = TextAnchor.MiddleLeft;
        dockTab.alignment = TextAnchor.MiddleLeft;
        dockTabActive.alignment = TextAnchor.MiddleLeft;

        foreach (var style in AllStyles)
        {
            style.fontSize = fontSize;
            SetText(style, palette.Text, palette.DisabledText);
        }

        miniLabel.fontSize = Math.Max(9, fontSize - 2);
        vectorAxisLabel.fontSize = Math.Clamp(fontSize - 4, 10, 14);
        largeLabel.fontSize = fontSize + 3;

        ApplyTransparent(label, palette.Text, palette.DisabledText);
        ApplyTransparent(vectorAxisLabel, palette.Text, palette.DisabledText);
        ApplyTransparent(boldLabel, palette.Text, palette.DisabledText);
        ApplyTransparent(miniLabel, palette.MutedText, palette.DisabledText);
        ApplyTransparent(largeLabel, palette.Text, palette.DisabledText);
        ApplyTransparent(inspectorDefaultMargins, palette.Text, palette.DisabledText);

        ApplySurface(textField, palette.Field, palette.FieldHover, palette.FieldFocused,
            palette.FieldFocused, palette.Border, palette.FocusBorder, palette.Text, palette.DisabledText);
        ApplySurface(textArea, palette.Field, palette.FieldHover, palette.FieldFocused,
            palette.FieldFocused, palette.Border, palette.FocusBorder, palette.Text, palette.DisabledText);
        ApplySurface(numberField, palette.Field, palette.FieldHover, palette.FieldFocused,
            palette.FieldFocused, palette.Border, palette.FocusBorder, palette.Text, palette.DisabledText);
        ApplySurface(toolbarSearchField, palette.Field, palette.FieldHover, palette.FieldFocused,
            palette.FieldFocused, palette.Border, palette.FocusBorder, palette.Text, palette.DisabledText);
        ApplySurface(popup, palette.Field, palette.FieldHover, palette.ButtonPressed,
            palette.FieldFocused, palette.Border, palette.FocusBorder, palette.Text, palette.DisabledText);
        ApplyTransparent(colorField, palette.Text, palette.DisabledText);
        ApplyTransparent(colorPickerSwatch, palette.Text, palette.DisabledText);

        ApplySurface(helpBox, palette.PanelRaised, palette.PanelRaised, palette.PanelRaised,
            palette.PanelRaised, palette.Border, palette.FocusBorder, palette.Text, palette.DisabledText);
        ApplySurface(toolbar, palette.Toolbar, palette.Toolbar, palette.Toolbar,
            palette.Toolbar, palette.Border, palette.Border, palette.Text, palette.DisabledText);
        ApplySurface(statusBar, palette.Toolbar, palette.Toolbar, palette.Toolbar,
            palette.Toolbar, palette.Border, palette.Border, palette.MutedText, palette.DisabledText);
        ApplySurface(windowTitle, palette.TitleBar, palette.Hover, palette.ButtonPressed,
            palette.TitleBar, palette.Border, palette.FocusBorder, palette.Text, palette.DisabledText);
        ApplySurface(inspectorTitlebar, palette.PanelRaised, palette.Hover, palette.ButtonPressed,
            palette.PanelRaised, palette.Border, palette.FocusBorder, palette.Text, palette.DisabledText);

        ApplySurface(toolbarButton, palette.Toolbar, palette.Hover, palette.ButtonPressed,
            palette.Hover, Transparent, palette.FocusBorder, palette.Text, palette.DisabledText, false);
        ApplySurface(toolbarIconButton, palette.Toolbar, palette.Hover, palette.ButtonPressed,
            palette.Hover, Transparent, palette.FocusBorder, palette.Text, palette.DisabledText, false);
        ApplySurface(toolbarIconButtonSelected, palette.Selection, palette.Active, palette.ButtonPressed,
            palette.Selection, Transparent, palette.FocusBorder, palette.Text, palette.DisabledText, false);
        ApplySurface(miniButton, palette.Button, palette.ButtonHover, palette.ButtonPressed,
            palette.ButtonPressed, palette.Border, palette.FocusBorder, palette.Text, palette.DisabledText);

        ApplySurface(dockTab, palette.TitleBar, palette.Hover, palette.ButtonPressed,
            palette.TitleBar, palette.Border, palette.FocusBorder, palette.MutedText, palette.DisabledText);
        ApplySurface(dockTabActive, palette.PanelRaised, palette.PanelRaised, palette.PanelRaised,
            palette.PanelRaised, palette.Border, palette.FocusBorder, palette.Text, palette.DisabledText);
        ApplySurface(menuItem, palette.PanelRaised, palette.Selection, palette.Selection,
            palette.Selection, Transparent, palette.FocusBorder, palette.Text, palette.DisabledText, false);
        ApplySurface(menuItemDisabled, palette.PanelRaised, palette.PanelRaised, palette.PanelRaised,
            palette.PanelRaised, Transparent, Transparent, palette.DisabledText, palette.DisabledText, false);

        ApplySurface(treeViewRow, Transparent, palette.Hover, palette.Hover,
            palette.Hover, Transparent, palette.FocusBorder, palette.Text, palette.DisabledText, false);
        ApplySurface(treeViewRowSelected, palette.Selection, palette.Selection, palette.Selection,
            palette.Selection, Transparent, palette.FocusBorder, palette.Text, palette.DisabledText, false);

        ApplySurface(hierarchySceneHeader, Transparent, Transparent, Transparent,
            Transparent, Transparent, Transparent, palette.Text, palette.DisabledText, false);
        ApplySurface(hierarchySceneHeaderActive, Transparent, Transparent, Transparent,
            Transparent, Transparent, Transparent, palette.Text, palette.DisabledText, false);
        ApplySurface(hierarchyRow, Transparent, Transparent, Transparent,
            Transparent, Transparent, Transparent, palette.Text, palette.DisabledText, false);
        ApplySurface(hierarchyRowSelected, Transparent, Transparent, Transparent,
            Transparent, Transparent, Transparent, palette.Text, palette.DisabledText, false);
        ApplySurface(hierarchyRowInactive, Transparent, Transparent, Transparent,
            Transparent, Transparent, Transparent, palette.DisabledText, palette.DisabledText, false);
        ApplySurface(hierarchyAction, Transparent, palette.Hover, palette.ButtonPressed,
            palette.Hover, Transparent, palette.FocusBorder, palette.Text, palette.DisabledText, false);

        ApplySurface(foldout, Transparent, palette.Hover, palette.ButtonPressed,
            palette.Hover, Transparent, palette.FocusBorder, palette.Text, palette.DisabledText, false);
        ApplyTransparent(linkLabel, palette.Accent, palette.DisabledText);
        linkLabel.active.textColor = palette.Text;
        linkLabel.hover.textColor = palette.Accent;

        ApplySurface(separator, palette.Border, palette.Border, palette.Border,
            palette.Border, Transparent, Transparent, palette.Text, palette.DisabledText, false);
    }

    private static IEnumerable<GUIStyle> AllStyles =>
    [
        label, vectorAxisLabel, boldLabel, miniLabel, largeLabel, textField, textArea, numberField, popup,
        colorField, colorPickerSwatch,
        helpBox, toolbar, toolbarButton, toolbarIconButton, toolbarIconButtonSelected, toolbarSearchField, dockTab,
        dockTabActive, windowTitle, inspectorTitlebar, menuItem, menuItemDisabled, miniButton,
        treeViewRow, treeViewRowSelected, hierarchySceneHeader, hierarchySceneHeaderActive, hierarchyRow,
        hierarchyRowSelected, hierarchyRowInactive, hierarchyAction, statusBar, foldout, linkLabel,
        inspectorDefaultMargins, separator
    ];

    private static void ApplyTransparent(GUIStyle style, Color text, Color disabledText)
        => ApplySurface(style, Transparent, Transparent, Transparent, Transparent,
            Transparent, Transparent, text, disabledText, false);

    private static void ApplySurface(GUIStyle style, Color normal, Color hover, Color active,
        Color focused, Color border, Color focusBorder, Color text, Color disabledText,
        bool bordered = true)
    {
        style.borderWidth = bordered ? Fix64.One : Fix64.Zero;
        SetState(style.normal, normal, text, border);
        SetState(style.hover, hover, text, border);
        SetState(style.active, active, text, focusBorder);
        SetState(style.focused, focused, text, focusBorder);
        SetState(style.disabled, normal, disabledText, border);
    }

    private static void SetText(GUIStyle style, Color text, Color disabledText)
    {
        style.normal.textColor = text;
        style.hover.textColor = text;
        style.active.textColor = text;
        style.focused.textColor = text;
        style.disabled.textColor = disabledText;
    }

    private static void SetState(GUIStyleState state, Color background, Color text, Color border)
        => (state.backgroundColor, state.textColor, state.borderColor) = (background, text, border);

    private static readonly Color Transparent = new(0, 0, 0, 0);
}
