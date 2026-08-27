using BEngine.Editor.Documents;

namespace BEngine.Editor;

public static class EditorAppearance
{
    public const float MinimumScale = 0.5f;
    public const float MaximumScale = 1.8f;
    public static EditorTheme theme { get; private set; } = EditorTheme.Dark;
    public static string fontFamily { get; private set; } = "BEngine Built-in";
    public const int DefaultFontSize = 14;
    public static int fontSize { get; private set; } = DefaultFontSize;
    public static GUISkin activeSkin => GUI.skin;
    public static IReadOnlyList<GUISkin> builtInSkins => BuiltInSkins;
    public static bool isDarkTheme
    {
        get
        {
            var background = activeSkin.window.normal.backgroundColor;
            return (double)(background.r * Fix64.FromDecimal(.2126m) +
                            background.g * Fix64.FromDecimal(.7152m) +
                            background.b * Fix64.FromDecimal(.0722m)) < .5;
        }
    }
    public static event Action? appearanceChanged;

    private static readonly GUISkin[] BuiltInSkins;

    static EditorAppearance()
    {
        var existingSkin = GUI.skin;
        var preserveExistingSkin = !ReferenceEquals(existingSkin, GUI.initialSkin);
        BuiltInSkins =
        [
            CreateSkin(nameof(EditorTheme.Light), LightPalette, fontSize, builtIn: true),
            CreateSkin(nameof(EditorTheme.Dark), DarkPalette, fontSize, builtIn: true),
            CreateSkin(nameof(EditorTheme.Classic), ClassicPalette, fontSize, builtIn: true)
        ];
        GUI.skin = preserveExistingSkin ? existingSkin : GetBuiltInSkin(EditorTheme.Dark);
        if (preserveExistingSkin) theme = EditorTheme.Custom;
    }

    public static void Apply(EditorPreferencesDocument preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        fontFamily = string.IsNullOrWhiteSpace(preferences.EditorFont)
            ? "BEngine Built-in" : preferences.EditorFont;
        // Font metrics are part of editor layout contracts. Older preference files are
        // normalized in memory instead of allowing a legacy size to distort every panel.
        preferences.EditorFontSize = DefaultFontSize;
        fontSize = DefaultFontSize;
        GUIUtility.fontFamily = fontFamily;
        preferences.EditorScale = float.IsFinite(preferences.EditorScale)
            ? Math.Clamp(preferences.EditorScale, MinimumScale, MaximumScale)
            : 1f;
        GUIUtility.pixelsPerPoint = (Fix64)preferences.EditorScale;
        SetSkin(ResolvePreferredSkin(preferences), notify: false);
        EditorLocalization.SetLocale(preferences.Locale);
        NotifyAppearanceChanged();
    }

    public static void SetSkin(GUISkin skin) => SetSkin(skin, notify: true);

    public static bool IsActiveSkin(GUISkin skin)
    {
        ArgumentNullException.ThrowIfNull(skin);
        if (ReferenceEquals(activeSkin, skin)) return true;
        var activePath = AssetDatabase.GetAssetPath(activeSkin);
        var candidatePath = AssetDatabase.GetAssetPath(skin);
        return activePath.Length > 0 && candidatePath.Length > 0 &&
               activePath.Equals(candidatePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void RefreshSkin(GUISkin skin)
    {
        ArgumentNullException.ThrowIfNull(skin);
        if (IsActiveSkin(skin)) SetSkin(skin);
    }

    private static void SetSkin(GUISkin skin, bool notify)
    {
        ArgumentNullException.ThrowIfNull(skin);
        if (skin.isBuiltIn && Enum.TryParse<EditorTheme>(skin.name, true, out var builtInPreset))
            ConfigureSkin(skin, GetPresetPalette(builtInPreset), fontSize);
        else skin.Apply();
        GUI.skin = skin;
        theme = skin.isBuiltIn && Enum.TryParse<EditorTheme>(skin.name, true, out var preset)
            ? preset
            : EditorTheme.Custom;
        if (notify) NotifyAppearanceChanged();
    }

    private static void NotifyAppearanceChanged()
    {
        EditorCallbackDispatcher.Invoke(appearanceChanged, nameof(appearanceChanged));
        EditorBridge.Host?.RepaintAllWindows();
    }

    private static GUISkin ResolvePreferredSkin(EditorPreferencesDocument preferences)
    {
        var token = preferences.EditorSkin?.Trim() ?? string.Empty;
        if (token.StartsWith(EditorSkinPreferences.BuiltInPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var requestedName = token[EditorSkinPreferences.BuiltInPrefix.Length..];
            if (BuiltInSkins.FirstOrDefault(skin =>
                    skin.name.Equals(requestedName, StringComparison.OrdinalIgnoreCase)) is { } builtIn)
                return builtIn;
        }
        else if (token.Length > 0)
        {
            try
            {
                if (BAsset.Load<GUISkin>(EditorSkinPreferences.ResolveTokenPath(token)) is { } custom)
                    return custom;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidDataException or FormatException or ArgumentException or
                                              NotSupportedException or YamlDotNet.Core.YamlException)
            {
                Debug.LogWarning($"Could not load GUI skin '{token}': {exception.Message}");
            }

            var fallback = GetBuiltInSkin(EditorTheme.Dark);
            preferences.EditorSkin = EditorSkinPreferences.GetToken(fallback);
            preferences.EditorTheme = nameof(EditorTheme.Dark);
            return fallback;
        }

        var legacyTheme = Enum.TryParse<EditorTheme>(preferences.EditorTheme, true, out var selected)
            ? selected
            : EditorTheme.Dark;
        if (legacyTheme is EditorTheme.Light or EditorTheme.Dark or EditorTheme.Classic)
        {
            var builtIn = GetBuiltInSkin(legacyTheme);
            preferences.EditorSkin = EditorSkinPreferences.GetToken(builtIn);
            return builtIn;
        }

        var legacyFallback = GetBuiltInSkin(EditorTheme.Dark);
        preferences.EditorSkin = EditorSkinPreferences.GetToken(legacyFallback);
        preferences.EditorTheme = nameof(EditorTheme.Dark);
        return legacyFallback;
    }

    private static GUISkin GetBuiltInSkin(EditorTheme preset) => BuiltInSkins.First(skin =>
        skin.name.Equals(preset.ToString(), StringComparison.OrdinalIgnoreCase));

    private static GUISkin CreateSkin(string name, EditorThemePalette colors, int size, bool builtIn)
    {
        var skin = new GUISkin { name = name };
        ConfigureSkin(skin, colors, size);
        if (builtIn) skin.MarkBuiltIn(name);
        return skin;
    }

    private static void ConfigureSkin(GUISkin skin, EditorThemePalette colors, int size)
        => skin.ApplyPaletteDefaults(colors, size);

    internal static EditorThemePalette GetPresetPalette(EditorTheme preset) => preset switch
    {
        EditorTheme.Dark => DarkPalette,
        EditorTheme.Light => LightPalette,
        EditorTheme.Classic => ClassicPalette,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset,
            "Only Dark, Light and Classic are built-in presets.")
    };

    private static Color C(float r, float g, float b, float a = 1) =>
        new((Fix64)r, (Fix64)g, (Fix64)b, (Fix64)a);
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
