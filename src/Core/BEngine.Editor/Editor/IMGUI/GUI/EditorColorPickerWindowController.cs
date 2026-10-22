using System.Globalization;

namespace BEngine.Editor;

public sealed class EditorColorPickerWindowController :
    EditorWindowController<EditorColorPickerWindow, EditorColorPickerWindowModel>
{
    private const string HexControlName = "ColorPickerHexadecimal";

    public EditorColorPickerWindowController(
        EditorWindowMvcContext context,
        EditorColorPickerWindowModel model) : base(context, model) { }

    public void Configure(Color value, bool showAlpha, bool hdr, Action<Color> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        Model.Original = value;
        Model.Value = value;
        Model.ShowAlpha = showAlpha;
        Model.Hdr = hdr;
        Model.Changed = changed;
        Model.Hex = ToHex(value);
    }

    public void SetMode(int mode, int modeCount) =>
        Model.Mode = Math.Clamp(mode, 0, Math.Max(0, modeCount - 1));

    public void SetDefaultsExpanded(bool expanded) => Model.DefaultsExpanded = expanded;

    public void SetSwatchesExpanded(bool expanded) => Model.SwatchesExpanded = expanded;

    public void Revert() => SetValue(Model.Original);

    public void SetValue(Color value) => SetValue(value, GUI.GetNameOfFocusedControl());

    public void SetValue(Color value, string focusedControl)
    {
        value = new Color(Fix64.Max(0, value.r), Fix64.Max(0, value.g), Fix64.Max(0, value.b),
            Model.ShowAlpha ? Fix64.Clamp(value.a, 0, 1) : Model.Original.a);
        if (Model.Value.Equals(value)) return;
        Model.Value = value;
        if (!string.Equals(focusedControl, HexControlName, StringComparison.Ordinal))
            Model.Hex = ToHex(value);
        if (Model.Changed is { } changed)
            EditorFeatureGuard.Invoke(View, "ColorChanged", () => changed(value));
        Repaint();
    }

    public void SetHex(string text, string focusedControl = HexControlName)
    {
        Model.Hex = (text ?? string.Empty).Trim().TrimStart('#').ToUpperInvariant();
        if (TryParseHex(Model.Hex, Model.Value.a, out var parsed))
            SetValue(parsed, focusedControl);
    }

    public void SetExposure(float exposure)
    {
        var peak = Math.Max(.0001f, Math.Max((float)Model.Value.r,
            Math.Max((float)Model.Value.g, (float)Model.Value.b)));
        var scale = (float)Math.Pow(2, Math.Clamp(exposure, -10, 10)) / peak;
        SetValue(new Color((Fix64)((float)Model.Value.r * scale),
            (Fix64)((float)Model.Value.g * scale),
            (Fix64)((float)Model.Value.b * scale), Model.Value.a));
    }

    public static string ToHex(Color color)
    {
        static byte Byte(Fix64 value) =>
            (byte)Math.Clamp((int)Math.Round((double)value * 255), 0, 255);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Byte(color.r):X2}{Byte(color.g):X2}{Byte(color.b):X2}");
    }

    public static bool TryParseHex(string text, Fix64 alpha, out Color color)
    {
        color = default;
        if (text.Length is not (6 or 8) || !text.All(Uri.IsHexDigit)) return false;
        if (!byte.TryParse(text.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(text.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(text.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
            return false;
        var parsedAlpha = alpha;
        if (text.Length == 8 && byte.TryParse(text.AsSpan(6, 2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var alphaByte)) parsedAlpha = (Fix64)(alphaByte / 255f);
        color = new Color((Fix64)(red / 255f), (Fix64)(green / 255f),
            (Fix64)(blue / 255f), parsedAlpha);
        return true;
    }
}
