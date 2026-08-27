using System.Collections;
using BEngine.Rendering;
using BEngine.Serialization;
using YamlDotNet.Serialization;

namespace BEngine.Editor;

[EditorIcon("Icons/Assets/AssetStyle.png")]
[CreateAssetMenu(fileName = "New GUI Skin", menuName = "GUI/GUISkin", order = 210)]
public sealed partial class GUISkin : BAsset, IEnumerable<GUIStyle>
{
    public const string FileExtension = ".guiskin.yaml";

    public GUIStyle label { get; set; } = Style("label", C(0, 0, 0, 0));
    public GUIStyle box { get; set; } = Style("box", C(0.17f, 0.17f, 0.17f, 1));
    public GUIStyle button { get; set; } = Style("button", C(0.27f, 0.27f, 0.27f, 1));
    public GUIStyle toggle { get; set; } = Style("toggle", C(0, 0, 0, 0));
    public GUIStyle textField { get; set; } = Style("textField", C(0.16f, 0.16f, 0.16f, 1));
    public GUIStyle textArea { get; set; } = Style("textArea", C(0.16f, 0.16f, 0.16f, 1));
    public GUIStyle window { get; set; } = Style("window", C(0.20f, 0.20f, 0.20f, 1));
    public GUIStyle horizontalSlider { get; set; } = Style("horizontalSlider", C(0.12f, 0.12f, 0.12f, 1));
    public GUIStyle horizontalSliderThumb { get; set; } = Style("horizontalSliderThumb", C(0.28f, 0.55f, 0.78f, 1));
    public GUIStyle verticalSlider { get; set; } = Style("verticalSlider", C(0.12f, 0.12f, 0.12f, 1));
    public GUIStyle verticalSliderThumb { get; set; } = Style("verticalSliderThumb", C(0.28f, 0.55f, 0.78f, 1));
    public GUIStyle horizontalScrollbar { get; set; } = Style("horizontalScrollbar", C(0.12f, 0.12f, 0.12f, 1));
    public GUIStyle horizontalScrollbarThumb { get; set; } = Style("horizontalScrollbarThumb", C(0.36f, 0.36f, 0.36f, 1));
    public GUIStyle horizontalScrollbarLeftButton { get; set; } = Style("horizontalScrollbarLeftButton", C(0.27f, 0.27f, 0.27f, 1));
    public GUIStyle horizontalScrollbarRightButton { get; set; } = Style("horizontalScrollbarRightButton", C(0.27f, 0.27f, 0.27f, 1));
    public GUIStyle verticalScrollbar { get; set; } = Style("verticalScrollbar", C(0.12f, 0.12f, 0.12f, 1));
    public GUIStyle verticalScrollbarThumb { get; set; } = Style("verticalScrollbarThumb", C(0.36f, 0.36f, 0.36f, 1));
    public GUIStyle verticalScrollbarUpButton { get; set; } = Style("verticalScrollbarUpButton", C(0.27f, 0.27f, 0.27f, 1));
    public GUIStyle verticalScrollbarDownButton { get; set; } = Style("verticalScrollbarDownButton", C(0.27f, 0.27f, 0.27f, 1));
    public GUIStyle scrollView { get; set; } = Style("scrollView", C(0, 0, 0, 0));

    // Editor-only named slots. EditorStyles resolves these from the active skin so a
    // single GUISkin asset owns the complete editor appearance.
    public GUIStyle vectorAxisLabel { get; set; } = Style("vectorAxisLabel");
    public GUIStyle boldLabel { get; set; } = Style("boldLabel");
    public GUIStyle centeredBoldLabel { get; set; } = new("centeredBoldLabel")
    {
        alignment = TextAnchor.MiddleCenter
    };
    public GUIStyle miniLabel { get; set; } = new("miniLabel") { fontSize = 11 };
    public GUIStyle centeredMiniLabel { get; set; } = new("centeredMiniLabel")
    {
        fontSize = 11,
        alignment = TextAnchor.MiddleCenter
    };
    public GUIStyle largeLabel { get; set; } = new("largeLabel") { fontSize = 16 };
    public GUIStyle numberField { get; set; } = Style("numberField");
    public GUIStyle popup { get; set; } = Style("popup");
    public GUIStyle dropDownButton { get; set; } = new("dropDownButton") { alignment = TextAnchor.MiddleLeft };
    public GUIStyle colorField { get; set; } = Style("colorField");
    public GUIStyle colorPickerSwatch { get; set; } = Style("colorPickerSwatch");
    public GUIStyle helpBox { get; set; } = new("helpBox") { wordWrap = true, fixedHeight = 38 };
    public GUIStyle toolbar { get; set; } = new("toolbar") { fixedHeight = 24 };
    public GUIStyle toolbarButton { get; set; } = new("toolbarButton") { fixedHeight = 22 };
    public GUIStyle toolbarIconButton { get; set; } = IconButton("toolbarIconButton");
    public GUIStyle toolbarIconButtonSelected { get; set; } = IconButton("toolbarIconButtonSelected");
    public GUIStyle toolbarSearchField { get; set; } = new("toolbarSearchField") { fixedHeight = 20 };
    public GUIStyle dockTab { get; set; } = new("dockTab") { fixedHeight = 24 };
    public GUIStyle dockTabActive { get; set; } = new("dockTabActive") { fixedHeight = 24 };
    public GUIStyle windowTitle { get; set; } = new("windowTitle") { fixedHeight = 24 };
    public GUIStyle inspectorTitlebar { get; set; } = new("inspectorTitlebar") { fixedHeight = 22 };
    public GUIStyle menuItem { get; set; } = new("menuItem") { fixedHeight = 22 };
    public GUIStyle menuItemDisabled { get; set; } = new("menuItemDisabled") { fixedHeight = 22 };
    public GUIStyle miniButton { get; set; } = new("miniButton") { fixedHeight = 20 };
    public GUIStyle treeViewRow { get; set; } = new("treeViewRow") { fixedHeight = 22 };
    public GUIStyle treeViewRowSelected { get; set; } = new("treeViewRowSelected") { fixedHeight = 22 };
    public GUIStyle hierarchySceneHeader { get; set; } = new("hierarchySceneHeader") { fixedHeight = 22 };
    public GUIStyle hierarchySceneHeaderActive { get; set; } = new("hierarchySceneHeaderActive") { fixedHeight = 22 };
    public GUIStyle hierarchyRow { get; set; } = new("hierarchyRow") { fixedHeight = 22 };
    public GUIStyle hierarchyRowSelected { get; set; } = new("hierarchyRowSelected") { fixedHeight = 22 };
    public GUIStyle hierarchyRowInactive { get; set; } = new("hierarchyRowInactive") { fixedHeight = 22 };
    public GUIStyle hierarchyAction { get; set; } = IconButton("hierarchyAction");
    public GUIStyle statusBar { get; set; } = new("statusBar") { fixedHeight = 20 };
    public GUIStyle foldout { get; set; } = Style("foldout");
    public GUIStyle linkLabel { get; set; } = Style("linkLabel");
    public GUIStyle inspectorDefaultMargins { get; set; } = Style("inspectorDefaultMargins");
    public GUIStyle separator { get; set; } = new("separator") { fixedHeight = 1 };

    public GUIStyle[] customStyles { get; set; } = [];

    [YamlIgnore, HideInInspector]
    public bool isBuiltIn { get; private set; }

    [YamlIgnore, HideInInspector]
    public bool isReadOnly => isBuiltIn;

    [YamlIgnore, HideInInspector]
    public static GUISkin current => GUI.skin;

    [YamlIgnore, HideInInspector]
    public IReadOnlyList<GUIStyle> styles => EnumerateBuiltInStyles()
        .Select(static pair => pair.Style).Concat(customStyles ?? []).ToArray();

    public GUISkin()
    {
        name = nameof(GUISkin);
        if (GUI.skin is not null)
            ApplyDefaultTheme();
        Apply();
    }

    public GUISkin(GUISkin other)
    {
        ArgumentNullException.ThrowIfNull(other);
        name = other.name;
        CopyFrom(other);
    }

    public GUISkin Clone(string? skinName = null)
    {
        var clone = new GUISkin(this);
        if (!string.IsNullOrWhiteSpace(skinName)) clone.name = skinName.Trim();
        return clone;
    }

    public void CopyFrom(GUISkin other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var source = other.EnumerateBuiltInStyles().ToDictionary(static pair => pair.Name,
            static pair => pair.Style, StringComparer.OrdinalIgnoreCase);
        foreach (var (slot, destination) in EnumerateBuiltInStyles())
            if (source.TryGetValue(slot, out var value)) destination.CopyFrom(value);
        customStyles = (other.customStyles ?? []).Where(static style => style is not null)
            .Select(static style => style.Clone()).ToArray();
        Apply();
    }

    public GUIStyle? FindStyle(string styleName)
    {
        if (string.IsNullOrWhiteSpace(styleName)) return null;
        var customStyleSnapshot = customStyles ?? [];
        for (var index = customStyleSnapshot.Length - 1; index >= 0; index--)
        {
            var custom = customStyleSnapshot[index];
            if (custom is not null && custom.name.Equals(styleName, StringComparison.OrdinalIgnoreCase))
                return custom;
        }
        foreach (var (slot, style) in EnumerateBuiltInStyles())
            if (slot.Equals(styleName, StringComparison.OrdinalIgnoreCase)) return style;
        return null;
    }

    public GUIStyle GetStyle(string styleName) => FindStyle(styleName) ??
        throw new ArgumentException($"GUIStyle '{styleName}' was not found in skin '{name}'.", nameof(styleName));

    public void AddStyle(GUIStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentException.ThrowIfNullOrWhiteSpace(style.name);
        var items = (customStyles ?? []).ToList();
        var index = items.FindIndex(item => item is not null && item.name.Equals(style.name,
            StringComparison.OrdinalIgnoreCase));
        if (index >= 0) items[index] = style;
        else items.Add(style);
        customStyles = items.ToArray();
    }

    public bool RemoveStyle(string styleName)
    {
        if (string.IsNullOrWhiteSpace(styleName) || customStyles is not { Length: > 0 }) return false;
        var remaining = customStyles.Where(style => style is null ||
            !style.name.Equals(styleName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (remaining.Length == customStyles.Length) return false;
        customStyles = remaining;
        return true;
    }

    public void Apply()
    {
        customStyles ??= [];
        foreach (var (slot, style) in EnumerateBuiltInStyles()) style.name = slot;
        foreach (var style in customStyles)
            if (style is not null) style.name = style.name?.Trim() ?? string.Empty;
    }

    internal void ApplyPaletteDefaults(EditorThemePalette colors, int size)
    {
        Configure(label, Transparent, colors.Text, size, disabledText: colors.DisabledText);
        Configure(box, colors.PanelRaised, colors.Text, size, border: colors.Border);
        Configure(button, colors.Button, colors.Text, size,
            colors.ButtonHover, colors.ButtonPressed, colors.ButtonPressed,
            colors.Border, colors.FocusBorder, colors.DisabledText);
        SetBackgroundImage(button, GUIStyleBackground.SegmentedButton);
        button.alignment = TextAnchor.MiddleCenter;
        Configure(toggle, colors.Field, colors.Text, size,
            colors.FieldHover, colors.ButtonPressed, colors.FieldFocused,
            colors.Border, colors.FocusBorder, colors.DisabledText);
        Configure(textField, colors.Field, colors.Text, size,
            colors.FieldHover, colors.FieldFocused, colors.FieldFocused,
            colors.Border, colors.FocusBorder, colors.DisabledText);
        Configure(textArea, colors.Field, colors.Text, size,
            colors.FieldHover, colors.FieldFocused, colors.FieldFocused,
            colors.Border, colors.FocusBorder, colors.DisabledText);
        Configure(window, colors.Window, colors.Text, size, border: colors.Border);
        Configure(horizontalSlider, colors.ScrollTrack, colors.Text, size,
            colors.ScrollTrack, colors.ScrollTrack, border: colors.Border);
        Configure(verticalSlider, colors.ScrollTrack, colors.Text, size,
            colors.ScrollTrack, colors.ScrollTrack, border: colors.Border);
        Configure(horizontalSliderThumb, colors.ScrollThumb, colors.Text, size,
            colors.ScrollThumbHover, colors.Accent, colors.Accent);
        Configure(verticalSliderThumb, colors.ScrollThumb, colors.Text, size,
            colors.ScrollThumbHover, colors.Accent, colors.Accent);
        Configure(horizontalScrollbar, colors.ScrollTrack, colors.Text, size);
        Configure(verticalScrollbar, colors.ScrollTrack, colors.Text, size);
        Configure(horizontalScrollbarThumb, colors.ScrollThumb, colors.Text, size,
            colors.ScrollThumbHover, colors.ScrollThumbHover);
        Configure(verticalScrollbarThumb, colors.ScrollThumb, colors.Text, size,
            colors.ScrollThumbHover, colors.ScrollThumbHover);
        Configure(horizontalScrollbarLeftButton, colors.Button, colors.Text, size,
            colors.ButtonHover, colors.ButtonPressed, border: colors.Border,
            focusBorder: colors.FocusBorder, disabledText: colors.DisabledText);
        Configure(horizontalScrollbarRightButton, colors.Button, colors.Text, size,
            colors.ButtonHover, colors.ButtonPressed, border: colors.Border,
            focusBorder: colors.FocusBorder, disabledText: colors.DisabledText);
        Configure(verticalScrollbarUpButton, colors.Button, colors.Text, size,
            colors.ButtonHover, colors.ButtonPressed, border: colors.Border,
            focusBorder: colors.FocusBorder, disabledText: colors.DisabledText);
        Configure(verticalScrollbarDownButton, colors.Button, colors.Text, size,
            colors.ButtonHover, colors.ButtonPressed, border: colors.Border,
            focusBorder: colors.FocusBorder, disabledText: colors.DisabledText);
        Configure(scrollView, Transparent, colors.Text, size);
        EditorStyles.ApplyAppearance(this, colors, size);
        Apply();
    }

    internal void ApplyDefaultTheme() => ApplyPaletteDefaults(DefaultPalette, EditorAppearance.DefaultFontSize);

    public void MakeCurrent() => EditorAppearance.SetSkin(this);

    public static GUISkin Load(string path)
    {
        var document = YamlUtility.Load<GUISkinFile>(path);
        if (document.Format != "BEngine.GUISkin" || document.Version != 1)
            throw new InvalidDataException("Unsupported GUISkin asset.");
        if (document.Styles is null || document.CustomStyles is null)
            throw new InvalidDataException("GUISkin styles cannot be null.");
        try
        {
            var fileName = Path.GetFileName(path);
            var displayName = fileName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)
                ? fileName[..^FileExtension.Length]
                : Path.GetFileNameWithoutExtension(fileName);
            var skin = new GUISkin { name = displayName };
            var destinations = skin.EnumerateBuiltInStyles().ToDictionary(static pair => pair.Name,
                static pair => pair.Style, StringComparer.OrdinalIgnoreCase);
            foreach (var (slot, style) in document.Styles)
                if (style is not null && destinations.TryGetValue(slot, out var destination))
                    style.CopyTo(destination);
            skin.customStyles = document.CustomStyles.Where(static style => style is not null)
                .Select(static style => style.ToStyle()).ToArray();
            skin.Apply();
            return skin;
        }
        catch (NullReferenceException exception)
        {
            throw new InvalidDataException("GUISkin contains a null style, state, or color value.", exception);
        }
    }

    public void Save(string path)
    {
        if (isReadOnly) throw new InvalidOperationException("Built-in GUI skins are read-only.");
        Apply();
        YamlUtility.Save(new GUISkinFile
        {
            Name = name,
            Styles = EnumerateBuiltInStyles().ToDictionary(static pair => pair.Name,
                static pair => GUIStyleFile.From(pair.Style), StringComparer.Ordinal),
            CustomStyles = customStyles.Where(static style => style is not null)
                .Select(GUIStyleFile.From).ToList()
        }, path);
    }

    public IEnumerator<GUIStyle> GetEnumerator() => styles.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal void MarkBuiltIn(string displayName)
    {
        name = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
        isBuiltIn = true;
        hideFlags |= HideFlags.NotEditable | HideFlags.DontSaveInEditor;
    }

    internal IEnumerable<(string Name, GUIStyle Style)> EnumerateBuiltInStyles()
    {
        yield return ("label", label);
        yield return ("box", box);
        yield return ("button", button);
        yield return ("toggle", toggle);
        yield return ("textField", textField);
        yield return ("textArea", textArea);
        yield return ("window", window);
        yield return ("horizontalSlider", horizontalSlider);
        yield return ("horizontalSliderThumb", horizontalSliderThumb);
        yield return ("verticalSlider", verticalSlider);
        yield return ("verticalSliderThumb", verticalSliderThumb);
        yield return ("horizontalScrollbar", horizontalScrollbar);
        yield return ("horizontalScrollbarThumb", horizontalScrollbarThumb);
        yield return ("horizontalScrollbarLeftButton", horizontalScrollbarLeftButton);
        yield return ("horizontalScrollbarRightButton", horizontalScrollbarRightButton);
        yield return ("verticalScrollbar", verticalScrollbar);
        yield return ("verticalScrollbarThumb", verticalScrollbarThumb);
        yield return ("verticalScrollbarUpButton", verticalScrollbarUpButton);
        yield return ("verticalScrollbarDownButton", verticalScrollbarDownButton);
        yield return ("scrollView", scrollView);
        yield return ("vectorAxisLabel", vectorAxisLabel);
        yield return ("boldLabel", boldLabel);
        yield return ("centeredBoldLabel", centeredBoldLabel);
        yield return ("miniLabel", miniLabel);
        yield return ("centeredMiniLabel", centeredMiniLabel);
        yield return ("largeLabel", largeLabel);
        yield return ("numberField", numberField);
        yield return ("popup", popup);
        yield return ("dropDownButton", dropDownButton);
        yield return ("colorField", colorField);
        yield return ("colorPickerSwatch", colorPickerSwatch);
        yield return ("helpBox", helpBox);
        yield return ("toolbar", toolbar);
        yield return ("toolbarButton", toolbarButton);
        yield return ("toolbarIconButton", toolbarIconButton);
        yield return ("toolbarIconButtonSelected", toolbarIconButtonSelected);
        yield return ("toolbarSearchField", toolbarSearchField);
        yield return ("dockTab", dockTab);
        yield return ("dockTabActive", dockTabActive);
        yield return ("windowTitle", windowTitle);
        yield return ("inspectorTitlebar", inspectorTitlebar);
        yield return ("menuItem", menuItem);
        yield return ("menuItemDisabled", menuItemDisabled);
        yield return ("miniButton", miniButton);
        yield return ("treeViewRow", treeViewRow);
        yield return ("treeViewRowSelected", treeViewRowSelected);
        yield return ("hierarchySceneHeader", hierarchySceneHeader);
        yield return ("hierarchySceneHeaderActive", hierarchySceneHeaderActive);
        yield return ("hierarchyRow", hierarchyRow);
        yield return ("hierarchyRowSelected", hierarchyRowSelected);
        yield return ("hierarchyRowInactive", hierarchyRowInactive);
        yield return ("hierarchyAction", hierarchyAction);
        yield return ("statusBar", statusBar);
        yield return ("foldout", foldout);
        yield return ("linkLabel", linkLabel);
        yield return ("inspectorDefaultMargins", inspectorDefaultMargins);
        yield return ("separator", separator);
        foreach (var style in EnumerateExtendedEditorStyles()) yield return style;
    }

    private static GUIStyle Style(string name, Color background = default)
    {
        var style = new GUIStyle(name);
        style.normal.backgroundColor = background;
        return style;
    }

    private static GUIStyle IconButton(string name) => new(name)
    {
        fixedWidth = 24,
        fixedHeight = 22,
        alignment = TextAnchor.MiddleCenter
    };

    private static void Configure(GUIStyle style, Color background, Color text, int size,
        Color? hover = null, Color? active = null, Color? focused = null,
        Color? border = null, Color? focusBorder = null, Color? disabledText = null)
    {
        style.fontSize = size;
        style.borderWidth = border.HasValue ? Fix64.One : Fix64.Zero;
        SetState(style.normal, background, text, border ?? Transparent);
        SetState(style.hover, hover ?? background, text, border ?? Transparent);
        SetState(style.active, active ?? background, text, focusBorder ?? border ?? Transparent);
        SetState(style.focused, focused ?? active ?? background, text, focusBorder ?? border ?? Transparent);
        SetState(style.onNormal, background, text, border ?? Transparent);
        SetState(style.onHover, hover ?? background, text, border ?? Transparent);
        SetState(style.onActive, active ?? background, text, focusBorder ?? border ?? Transparent);
        SetState(style.onFocused, focused ?? active ?? background, text,
            focusBorder ?? border ?? Transparent);
        SetState(style.disabled, background, disabledText ?? text, border ?? Transparent);
    }

    private static void SetState(GUIStyleState state, Color background, Color text, Color border)
        => (state.backgroundColor, state.textColor, state.borderColor) = (background, text, border);

    private static void SetBackgroundImage(GUIStyle style, Texture? image)
    {
        style.normal.backgroundImage = image;
        style.hover.backgroundImage = image;
        style.active.backgroundImage = image;
        style.focused.backgroundImage = image;
        style.onNormal.backgroundImage = image;
        style.onHover.backgroundImage = image;
        style.onActive.backgroundImage = image;
        style.onFocused.backgroundImage = image;
        style.disabled.backgroundImage = image;
    }

    private static Color C(float r, float g, float b, float a) =>
        new((Fix64)r, (Fix64)g, (Fix64)b, (Fix64)a);

    private static readonly EditorThemePalette DefaultPalette = new(
        C(.169f, .169f, .169f, 1), C(.200f, .200f, .200f, 1), C(.145f, .145f, .145f, 1),
        C(.125f, .125f, .125f, 1), C(.310f, .310f, .310f, 1), C(.275f, .275f, .275f, 1),
        C(.176f, .365f, .529f, 1), C(.824f, .824f, .824f, 1), C(.604f, .604f, .604f, 1),
        C(.247f, .561f, .788f, 1), C(.098f, .098f, .098f, 1), C(.227f, .227f, .227f, 1),
        C(.165f, .165f, .165f, 1), C(.153f, .153f, .153f, 1), C(.125f, .125f, .125f, 1),
        C(.365f, .365f, .365f, 1), C(.235f, .235f, .235f, 1), C(.408f, .408f, .408f, 1),
        C(.337f, .525f, .710f, 1), C(.176f, .365f, .529f, 1), C(.282f, .282f, .282f, 1),
        C(.137f, .137f, .137f, 1), C(.357f, .357f, .357f, 1), C(.439f, .439f, .439f, 1),
        C(.047f, .047f, .047f, .92f));
    private static readonly Color Transparent = C(0, 0, 0, 0);

    private sealed class GUISkinFile
    {
        public string Format { get; set; } = "BEngine.GUISkin";
        public int Version { get; set; } = 1;
        public string Name { get; set; } = nameof(GUISkin);
        public Dictionary<string, GUIStyleFile> Styles { get; set; } = new(StringComparer.Ordinal);
        public List<GUIStyleFile> CustomStyles { get; set; } = [];
    }

    private sealed class GUIStyleFile
    {
        public string Name { get; set; } = string.Empty;
        public GUIStyleStateFile Normal { get; set; } = new();
        public GUIStyleStateFile Hover { get; set; } = new();
        public GUIStyleStateFile Active { get; set; } = new();
        public GUIStyleStateFile Focused { get; set; } = new();
        public GUIStyleStateFile OnNormal { get; set; } = new();
        public GUIStyleStateFile OnHover { get; set; } = new();
        public GUIStyleStateFile OnActive { get; set; } = new();
        public GUIStyleStateFile OnFocused { get; set; } = new();
        public GUIStyleStateFile Disabled { get; set; } = new();
        public long FixedWidth { get; set; }
        public long FixedHeight { get; set; }
        public long BorderWidth { get; set; }
        public long FontSize { get; set; }
        public TextAnchor Alignment { get; set; }
        public bool WordWrap { get; set; }
        public bool RichText { get; set; }
        public bool StretchWidth { get; set; }
        public bool StretchHeight { get; set; }

        internal static GUIStyleFile From(GUIStyle value) => new()
        {
            Name = value.name,
            Normal = GUIStyleStateFile.From(value.normal),
            Hover = GUIStyleStateFile.From(value.hover),
            Active = GUIStyleStateFile.From(value.active),
            Focused = GUIStyleStateFile.From(value.focused),
            OnNormal = GUIStyleStateFile.From(value.onNormal),
            OnHover = GUIStyleStateFile.From(value.onHover),
            OnActive = GUIStyleStateFile.From(value.onActive),
            OnFocused = GUIStyleStateFile.From(value.onFocused),
            Disabled = GUIStyleStateFile.From(value.disabled),
            FixedWidth = value.fixedWidth.RawValue,
            FixedHeight = value.fixedHeight.RawValue,
            BorderWidth = value.borderWidth.RawValue,
            FontSize = value.fontSize.RawValue,
            Alignment = value.alignment,
            WordWrap = value.wordWrap,
            RichText = value.richText,
            StretchWidth = value.stretchWidth,
            StretchHeight = value.stretchHeight
        };

        internal GUIStyle ToStyle()
        {
            var style = new GUIStyle();
            CopyTo(style);
            return style;
        }

        internal void CopyTo(GUIStyle destination)
        {
            destination.name = Name;
            Normal.CopyTo(destination.normal);
            Hover.CopyTo(destination.hover);
            Active.CopyTo(destination.active);
            Focused.CopyTo(destination.focused);
            OnNormal.CopyTo(destination.onNormal);
            OnHover.CopyTo(destination.onHover);
            OnActive.CopyTo(destination.onActive);
            OnFocused.CopyTo(destination.onFocused);
            Disabled.CopyTo(destination.disabled);
            destination.fixedWidth = Fix64.FromRaw(FixedWidth);
            destination.fixedHeight = Fix64.FromRaw(FixedHeight);
            destination.borderWidth = Fix64.FromRaw(BorderWidth);
            destination.fontSize = Fix64.FromRaw(FontSize);
            destination.alignment = Alignment;
            destination.wordWrap = WordWrap;
            destination.richText = RichText;
            destination.stretchWidth = StretchWidth;
            destination.stretchHeight = StretchHeight;
        }
    }

    private sealed class GUIStyleStateFile
    {
        public ColorFile TextColor { get; set; } = new();
        public ColorFile BackgroundColor { get; set; } = new();
        public ColorFile BorderColor { get; set; } = new();
        public string BackgroundImage { get; set; } = string.Empty;

        internal static GUIStyleStateFile From(GUIStyleState value) => new()
        {
            TextColor = ColorFile.From(value.textColor),
            BackgroundColor = ColorFile.From(value.backgroundColor),
            BorderColor = ColorFile.From(value.borderColor),
            BackgroundImage = GUIStyleBackground.ToReference(value.backgroundImage)
        };

        internal void CopyTo(GUIStyleState destination)
        {
            destination.textColor = TextColor.ToColor();
            destination.backgroundColor = BackgroundColor.ToColor();
            destination.borderColor = BorderColor.ToColor();
            destination.backgroundImage = GUIStyleBackground.FromReference(BackgroundImage);
        }
    }

    private sealed class ColorFile
    {
        public long R { get; set; }
        public long G { get; set; }
        public long B { get; set; }
        public long A { get; set; }

        internal static ColorFile From(Color value) => new()
        {
            R = value.r.RawValue,
            G = value.g.RawValue,
            B = value.b.RawValue,
            A = value.a.RawValue
        };

        internal Color ToColor() => new(Fix64.FromRaw(R), Fix64.FromRaw(G), Fix64.FromRaw(B), Fix64.FromRaw(A));
    }
}
