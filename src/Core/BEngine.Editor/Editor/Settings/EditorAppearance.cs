using BEngine.Editor.Documents;

namespace BEngine.Editor;

public static class EditorAppearance
{
    public static EditorTheme theme { get; private set; } = EditorTheme.Dark;
    public static string fontFamily { get; private set; } = "BEngine Built-in";
    public static int fontSize { get; private set; } = 13;
    public static EditorThemePalette palette { get; private set; }
    public static event Action? appearanceChanged;

    static EditorAppearance()
    {
        palette = DarkPalette;
        GUI.skin = CreateSkin(palette, fontSize);
        EditorStyles.ApplyAppearance(palette, fontSize);
    }

    public static void Apply(EditorPreferencesDocument preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        theme = Enum.TryParse<EditorTheme>(preferences.EditorTheme, true, out var selected)
            ? selected : EditorTheme.Dark;
        fontFamily = string.IsNullOrWhiteSpace(preferences.EditorFont)
            ? "BEngine Built-in" : preferences.EditorFont;
        fontSize = Math.Clamp(preferences.EditorFontSize, 10, 24);
        GUIUtility.fontFamily = fontFamily;
        GUIUtility.pixelsPerPoint = (Fix64)Math.Clamp(preferences.EditorScale, 0.75f, 2f);
        palette = theme switch
        {
            EditorTheme.Light => LightPalette,
            EditorTheme.Classic => ClassicPalette,
            _ => DarkPalette
        };
        GUI.skin = CreateSkin(palette, fontSize);
        EditorStyles.ApplyAppearance(palette, fontSize);
        EditorLocalization.SetLocale(preferences.Locale);
        EditorCallbackDispatcher.Invoke(appearanceChanged, nameof(appearanceChanged));
        EditorBridge.Host?.RepaintAllWindows();
    }

    private static GUISkin CreateSkin(EditorThemePalette colors, int size)
    {
        var skin = new GUISkin();
        Configure(skin.label, Transparent, colors.Text, size, disabledText: colors.DisabledText);
        Configure(skin.box, colors.PanelRaised, colors.Text, size, border: colors.Border);
        Configure(skin.button, colors.Button, colors.Text, size,
            colors.ButtonHover, colors.ButtonPressed, colors.ButtonPressed,
            colors.Border, colors.FocusBorder, colors.DisabledText);
        skin.button.alignment = TextAnchor.MiddleCenter;
        Configure(skin.toggle, Transparent, colors.Text, size,
            colors.Hover, colors.ButtonPressed, colors.ButtonPressed,
            disabledText: colors.DisabledText);
        Configure(skin.textField, colors.Field, colors.Text, size,
            colors.FieldHover, colors.FieldFocused, colors.FieldFocused,
            colors.Border, colors.FocusBorder, colors.DisabledText);
        Configure(skin.textArea, colors.Field, colors.Text, size,
            colors.FieldHover, colors.FieldFocused, colors.FieldFocused,
            colors.Border, colors.FocusBorder, colors.DisabledText);
        Configure(skin.window, colors.Window, colors.Text, size, border: colors.Border);
        Configure(skin.horizontalSlider, colors.ScrollTrack, colors.Text, size,
            colors.ScrollTrack, colors.ScrollTrack, border: colors.Border);
        Configure(skin.verticalSlider, colors.ScrollTrack, colors.Text, size,
            colors.ScrollTrack, colors.ScrollTrack, border: colors.Border);
        Configure(skin.horizontalSliderThumb, colors.ScrollThumb, colors.Text, size,
            colors.ScrollThumbHover, colors.Accent, colors.Accent);
        Configure(skin.verticalSliderThumb, colors.ScrollThumb, colors.Text, size,
            colors.ScrollThumbHover, colors.Accent, colors.Accent);
        Configure(skin.horizontalScrollbar, colors.ScrollTrack, colors.Text, size);
        Configure(skin.verticalScrollbar, colors.ScrollTrack, colors.Text, size);
        Configure(skin.horizontalScrollbarThumb, colors.ScrollThumb, colors.Text, size,
            colors.ScrollThumbHover, colors.ScrollThumbHover);
        Configure(skin.verticalScrollbarThumb, colors.ScrollThumb, colors.Text, size,
            colors.ScrollThumbHover, colors.ScrollThumbHover);
        return skin;
    }

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
        SetState(style.disabled, background, disabledText ?? text, border ?? Transparent);
    }

    private static void SetState(GUIStyleState state, Color background, Color text, Color border)
        => (state.backgroundColor, state.textColor, state.borderColor) = (background, text, border);

    private static Color C(float r, float g, float b, float a = 1) =>
        new((Fix64)r, (Fix64)g, (Fix64)b, (Fix64)a);
    private static readonly Color Transparent = C(0, 0, 0, 0);
    // Unity 2022-style neutral dark hierarchy: chrome, title bars, content and fields remain distinct.
    private static readonly EditorThemePalette DarkPalette = new(
        Window: C(.169f, .169f, .169f),
        Panel: C(.200f, .200f, .200f),
        Toolbar: C(.145f, .145f, .145f),
        Field: C(.125f, .125f, .125f),
        Button: C(.310f, .310f, .310f),
        Hover: C(.275f, .275f, .275f),
        Active: C(.176f, .365f, .529f),
        Text: C(.824f, .824f, .824f),
        MutedText: C(.604f, .604f, .604f),
        Accent: C(.247f, .561f, .788f),
        Border: C(.098f, .098f, .098f),
        PanelRaised: C(.227f, .227f, .227f),
        TitleBar: C(.165f, .165f, .165f),
        FieldHover: C(.153f, .153f, .153f),
        FieldFocused: C(.125f, .125f, .125f),
        ButtonHover: C(.365f, .365f, .365f),
        ButtonPressed: C(.235f, .235f, .235f),
        DisabledText: C(.408f, .408f, .408f),
        FocusBorder: C(.337f, .525f, .710f),
        Selection: C(.176f, .365f, .529f),
        SelectionInactive: C(.282f, .282f, .282f),
        ScrollTrack: C(.137f, .137f, .137f),
        ScrollThumb: C(.357f, .357f, .357f),
        ScrollThumbHover: C(.439f, .439f, .439f),
        Shadow: C(.047f, .047f, .047f, .92f));

    private static readonly EditorThemePalette LightPalette = new(
        Window: C(.760f, .760f, .760f),
        Panel: C(.820f, .820f, .820f),
        Toolbar: C(.690f, .690f, .690f),
        Field: C(.930f, .930f, .930f),
        Button: C(.760f, .760f, .760f),
        Hover: C(.710f, .780f, .840f),
        Active: C(.310f, .560f, .780f),
        Text: C(.110f, .110f, .110f),
        MutedText: C(.350f, .350f, .350f),
        Accent: C(.145f, .455f, .745f),
        Border: C(.500f, .500f, .500f),
        PanelRaised: C(.855f, .855f, .855f),
        TitleBar: C(.720f, .720f, .720f),
        FieldHover: C(.970f, .970f, .970f),
        FieldFocused: C(.960f, .960f, .960f),
        ButtonHover: C(.820f, .820f, .820f),
        ButtonPressed: C(.650f, .650f, .650f),
        DisabledText: C(.530f, .530f, .530f),
        FocusBorder: C(.180f, .480f, .760f),
        Selection: C(.310f, .560f, .780f),
        SelectionInactive: C(.650f, .690f, .720f),
        ScrollTrack: C(.700f, .700f, .700f),
        ScrollThumb: C(.470f, .470f, .470f),
        ScrollThumbHover: C(.380f, .380f, .380f),
        Shadow: C(.160f, .160f, .160f, .55f));

    private static readonly EditorThemePalette ClassicPalette = new(
        Window: C(.190f, .200f, .210f),
        Panel: C(.230f, .240f, .250f),
        Toolbar: C(.155f, .165f, .175f),
        Field: C(.135f, .145f, .155f),
        Button: C(.305f, .315f, .325f),
        Hover: C(.335f, .355f, .375f),
        Active: C(.190f, .430f, .630f),
        Text: C(.860f, .860f, .860f),
        MutedText: C(.640f, .650f, .660f),
        Accent: C(.280f, .570f, .820f),
        Border: C(.080f, .085f, .090f),
        PanelRaised: C(.265f, .275f, .285f),
        TitleBar: C(.175f, .185f, .195f),
        FieldHover: C(.165f, .175f, .185f),
        FieldFocused: C(.140f, .150f, .160f),
        ButtonHover: C(.375f, .390f, .405f),
        ButtonPressed: C(.230f, .245f, .260f),
        DisabledText: C(.430f, .440f, .450f),
        FocusBorder: C(.330f, .570f, .770f),
        Selection: C(.190f, .430f, .630f),
        SelectionInactive: C(.300f, .320f, .340f),
        ScrollTrack: C(.120f, .130f, .140f),
        ScrollThumb: C(.380f, .395f, .410f),
        ScrollThumbHover: C(.460f, .480f, .500f),
        Shadow: C(.045f, .050f, .055f, .90f));
}
