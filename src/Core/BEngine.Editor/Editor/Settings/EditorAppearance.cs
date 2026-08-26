using System.Globalization;
using BEngine.Editor.Documents;

namespace BEngine.Editor;

public static class EditorAppearance
{
    public static EditorTheme theme { get; private set; } = EditorTheme.Dark;
    public static string fontFamily { get; private set; } = "BEngine Built-in";
    public const int DefaultFontSize = 14;
    public static int fontSize { get; private set; } = DefaultFontSize;
    public static GUISkin activeSkin => GUI.skin;
    public static IReadOnlyList<GUISkin> builtInSkins => BuiltInSkins;
    public static EditorThemePalette palette => activeSkin.palette;
    public static bool isDarkTheme => (double)(palette.Window.r * Fix64.FromDecimal(.2126m) +
        palette.Window.g * Fix64.FromDecimal(.7152m) + palette.Window.b * Fix64.FromDecimal(.0722m)) < .5;
    public static IReadOnlyList<string> customThemeColorNames => PaletteColorNames;
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
        GUIUtility.pixelsPerPoint = (Fix64)Math.Clamp(preferences.EditorScale, 0.75f, 2f);
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
        if (skin.isBuiltIn) ConfigureSkin(skin, skin.palette, fontSize);
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
                if (BAsset.Load<GUISkin>(token) is { } custom) return custom;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException or
                                              YamlDotNet.Core.YamlException)
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

        // Preserve an older inline Custom palette until the user saves it as a GUISkin asset.
        return CreateSkin("Legacy Custom", GetCustomThemePalette(preferences), fontSize, builtIn: false);
    }

    private static GUISkin GetBuiltInSkin(EditorTheme preset) => BuiltInSkins.First(skin =>
        skin.name.Equals(preset.ToString(), StringComparison.OrdinalIgnoreCase));

    private static GUISkin CreateSkin(string name, EditorThemePalette colors, int size, bool builtIn)
    {
        var skin = new GUISkin { name = name, palette = colors };
        ConfigureSkin(skin, colors, size);
        if (builtIn) skin.MarkBuiltIn(name);
        return skin;
    }

    private static void ConfigureSkin(GUISkin skin, EditorThemePalette colors, int size)
        => skin.ApplyPaletteDefaults(colors, size);

    public static EditorThemePalette GetPresetPalette(EditorTheme preset) => preset switch
    {
        EditorTheme.Dark => DarkPalette,
        EditorTheme.Light => LightPalette,
        EditorTheme.Classic => ClassicPalette,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset,
            "Only Dark, Light and Classic are built-in presets.")
    };

    public static EditorThemePalette GetCustomThemePalette(EditorPreferencesDocument preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var colors = preferences.CustomThemeColors ??= new Dictionary<string, string>(StringComparer.Ordinal);
        var fallback = DarkPalette;
        return new EditorThemePalette(
            ReadColor(colors, nameof(EditorThemePalette.Window), fallback.Window),
            ReadColor(colors, nameof(EditorThemePalette.Panel), fallback.Panel),
            ReadColor(colors, nameof(EditorThemePalette.Toolbar), fallback.Toolbar),
            ReadColor(colors, nameof(EditorThemePalette.Field), fallback.Field),
            ReadColor(colors, nameof(EditorThemePalette.Button), fallback.Button),
            ReadColor(colors, nameof(EditorThemePalette.Hover), fallback.Hover),
            ReadColor(colors, nameof(EditorThemePalette.Active), fallback.Active),
            ReadColor(colors, nameof(EditorThemePalette.Text), fallback.Text),
            ReadColor(colors, nameof(EditorThemePalette.MutedText), fallback.MutedText),
            ReadColor(colors, nameof(EditorThemePalette.Accent), fallback.Accent),
            ReadColor(colors, nameof(EditorThemePalette.Border), fallback.Border),
            ReadColor(colors, nameof(EditorThemePalette.PanelRaised), fallback.PanelRaised),
            ReadColor(colors, nameof(EditorThemePalette.TitleBar), fallback.TitleBar),
            ReadColor(colors, nameof(EditorThemePalette.FieldHover), fallback.FieldHover),
            ReadColor(colors, nameof(EditorThemePalette.FieldFocused), fallback.FieldFocused),
            ReadColor(colors, nameof(EditorThemePalette.ButtonHover), fallback.ButtonHover),
            ReadColor(colors, nameof(EditorThemePalette.ButtonPressed), fallback.ButtonPressed),
            ReadColor(colors, nameof(EditorThemePalette.DisabledText), fallback.DisabledText),
            ReadColor(colors, nameof(EditorThemePalette.FocusBorder), fallback.FocusBorder),
            ReadColor(colors, nameof(EditorThemePalette.Selection), fallback.Selection),
            ReadColor(colors, nameof(EditorThemePalette.SelectionInactive), fallback.SelectionInactive),
            ReadColor(colors, nameof(EditorThemePalette.ScrollTrack), fallback.ScrollTrack),
            ReadColor(colors, nameof(EditorThemePalette.ScrollThumb), fallback.ScrollThumb),
            ReadColor(colors, nameof(EditorThemePalette.ScrollThumbHover), fallback.ScrollThumbHover),
            ReadColor(colors, nameof(EditorThemePalette.Shadow), fallback.Shadow));
    }

    public static void SetCustomThemePreset(EditorPreferencesDocument preferences, EditorTheme preset)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.CustomThemeColors = EnumeratePalette(GetPresetPalette(preset))
            .ToDictionary(static item => item.Name, static item => EncodeColor(item.Color),
                StringComparer.Ordinal);
        preferences.EditorTheme = nameof(EditorTheme.Custom);
        preferences.EditorSkin = string.Empty;
    }

    public static Color GetCustomThemeColor(EditorPreferencesDocument preferences, string name)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!PaletteColorNames.Contains(name, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown editor theme color.");
        var paletteValue = GetCustomThemePalette(preferences);
        return EnumeratePalette(paletteValue).FirstOrDefault(item => item.Name == name).Color;
    }

    public static void SetCustomThemeColor(EditorPreferencesDocument preferences, string name, Color color)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!PaletteColorNames.Contains(name, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown editor theme color.");
        var colors = preferences.CustomThemeColors ??= new Dictionary<string, string>(StringComparer.Ordinal);
        if (colors.Count == 0)
            foreach (var item in EnumeratePalette(DarkPalette)) colors[item.Name] = EncodeColor(item.Color);
        colors[name] = EncodeColor(color);
        preferences.EditorTheme = nameof(EditorTheme.Custom);
        preferences.EditorSkin = string.Empty;
    }

    private static Color ReadColor(IReadOnlyDictionary<string, string> colors, string name, Color fallback) =>
        colors.TryGetValue(name, out var encoded) && TryDecodeColor(encoded, out var parsed) ? parsed : fallback;

    private static string EncodeColor(Color color)
    {
        static byte Byte(Fix64 value) => (byte)Math.Clamp((int)Math.Round((double)value * 255), 0, 255);
        return string.Create(CultureInfo.InvariantCulture,
            $"#{Byte(color.r):X2}{Byte(color.g):X2}{Byte(color.b):X2}{Byte(color.a):X2}");
    }

    private static bool TryDecodeColor(string? encoded, out Color color)
    {
        color = default;
        var value = encoded?.Trim().TrimStart('#') ?? string.Empty;
        if (value.Length is not (6 or 8) || !value.All(Uri.IsHexDigit)) return false;
        if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
            return false;
        if (value.Length == 6) packed = packed << 8 | 0xFF;
        color = new Color((Fix64)(((packed >> 24) & 0xFF) / 255f),
            (Fix64)(((packed >> 16) & 0xFF) / 255f),
            (Fix64)(((packed >> 8) & 0xFF) / 255f), (Fix64)((packed & 0xFF) / 255f));
        return true;
    }

    private static IEnumerable<(string Name, Color Color)> EnumeratePalette(EditorThemePalette value) =>
    [
        (nameof(EditorThemePalette.Window), value.Window),
        (nameof(EditorThemePalette.Panel), value.Panel),
        (nameof(EditorThemePalette.Toolbar), value.Toolbar),
        (nameof(EditorThemePalette.Field), value.Field),
        (nameof(EditorThemePalette.Button), value.Button),
        (nameof(EditorThemePalette.Hover), value.Hover),
        (nameof(EditorThemePalette.Active), value.Active),
        (nameof(EditorThemePalette.Text), value.Text),
        (nameof(EditorThemePalette.MutedText), value.MutedText),
        (nameof(EditorThemePalette.Accent), value.Accent),
        (nameof(EditorThemePalette.Border), value.Border),
        (nameof(EditorThemePalette.PanelRaised), value.PanelRaised),
        (nameof(EditorThemePalette.TitleBar), value.TitleBar),
        (nameof(EditorThemePalette.FieldHover), value.FieldHover),
        (nameof(EditorThemePalette.FieldFocused), value.FieldFocused),
        (nameof(EditorThemePalette.ButtonHover), value.ButtonHover),
        (nameof(EditorThemePalette.ButtonPressed), value.ButtonPressed),
        (nameof(EditorThemePalette.DisabledText), value.DisabledText),
        (nameof(EditorThemePalette.FocusBorder), value.FocusBorder),
        (nameof(EditorThemePalette.Selection), value.Selection),
        (nameof(EditorThemePalette.SelectionInactive), value.SelectionInactive),
        (nameof(EditorThemePalette.ScrollTrack), value.ScrollTrack),
        (nameof(EditorThemePalette.ScrollThumb), value.ScrollThumb),
        (nameof(EditorThemePalette.ScrollThumbHover), value.ScrollThumbHover),
        (nameof(EditorThemePalette.Shadow), value.Shadow)
    ];

    private static readonly string[] PaletteColorNames = EnumeratePalette(DarkPalette)
        .Select(static item => item.Name).ToArray();

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
