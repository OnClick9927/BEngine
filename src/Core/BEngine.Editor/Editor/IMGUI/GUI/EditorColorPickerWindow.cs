using System.Globalization;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/Inspector.png")]
internal sealed class EditorColorPickerWindow : EditorWindow
{
    private const string HexControlName = "ColorPickerHexadecimal";
    private static readonly Color[] Palette =
    [
        Color.white, Color.black, Color.red, Color.green, Color.blue,
        Rgb(1f, .5f, 0f), Rgb(1f, 1f, 0f), Rgb(0f, 1f, 1f),
        Rgb(1f, 0f, 1f), Rgb(.5f, .5f, .5f), Rgb(.25f, .5f, 1f),
        Rgb(.55f, .25f, .8f), Rgb(.3f, .7f, .35f), Rgb(.8f, .3f, .25f)
    ];
    private Action<Color>? _changed;
    private Color _original = Color.white;
    private Color _value = Color.white;
    private bool _showAlpha = true;
    private bool _hdr;
    private int _mode = 1;
    private string _hex = "FFFFFF";
    private bool _defaultsExpanded = true;
    private bool _swatchesExpanded = true;

    private EditorColorPickerWindow()
    {
        saveToLayout = false;
        wantsMouseMove = true;
    }

    internal static void Open(Color value, bool showAlpha, bool hdr, Action<Color> changed)
    {
        var window = EnumerateOpenWindows().OfType<EditorColorPickerWindow>().FirstOrDefault() ??
                     (EditorColorPickerWindow)ScriptableObject.CreateInstance(typeof(EditorColorPickerWindow));
        window._original = value;
        window._value = value;
        window._showAlpha = showAlpha;
        window._hdr = hdr;
        window._changed = changed;
        window._hex = ToHex(value);
        var height = hdr ? 560 : showAlpha ? 510 : 486;
        window.position = new Rect(window.position.x, window.position.y, 360, height);
        window.minSize = new Vector2(300, hdr ? 520 : showAlpha ? 470 : 446);
        window.maxSize = new Vector2(520, 680);
        window.titleContent = new GUIContent(hdr ? "HDR Color" : "Color", window.titleContent.image,
            hdr ? "HDR Color Picker" : "Color Picker");
        window.ShowAuxWindow();
        window.Focus();
    }

    protected override void OnGUI()
    {
        if (HandleWindowKeys()) return;
        var width = Fix64.Max(280, GUIUtility.currentViewWidth);
        DrawHeader(new Rect(10, 10, width - 20, 34));

        var modes = _hdr
            ? new[] { "RGB 0-255", "RGB 0-1.0", "RGB 0-Inf", "HSV" }
            : new[] { "RGB 0-255", "RGB 0-1.0", "HSV" };
        _mode = EditorGUI.Popup(new Rect(10, 51, width - 20, 20), "Mode",
            Math.Clamp(_mode, 0, modes.Length - 1), modes);

        EditorColorMath.RgbToHsv(_value, out var hue, out var saturation, out var brightness);
        var svRect = new Rect(10, 78, width - 20, 132);
        var hueRect = new Rect(10, 216, width - 20, 14);
        DrawSaturationValue(svRect, hue, saturation, brightness);
        DrawHue(hueRect, hue);
        var svChanged = HandleSaturationValue(svRect, ref saturation, ref brightness);
        var hueChanged = HandleHorizontalValue(hueRect, ref hue, "ColorPickerHueStrip");
        if (svChanged || hueChanged)
            SetValue(EditorColorMath.HsvToRgb(hue, saturation, brightness, _value.a));

        EditorColorMath.RgbToHsv(_value, out hue, out saturation, out brightness);
        var channelY = (Fix64)238;
        EditorGUI.BeginChangeCheck();
        var next = DrawChannels(new Rect(10, channelY, width - 20, 20), modes.Length - 1,
            hue, saturation, brightness);
        if (EditorGUI.EndChangeCheck()) SetValue(next);

        var channelCount = _showAlpha ? 4 : 3;
        var detailsY = channelY + channelCount * 24 + 4;
        if (_hdr) DrawIntensity(new Rect(10, detailsY, width - 20, 20));
        else DrawHexadecimal(new Rect(10, detailsY, width - 20, 20));
        var foldoutY = detailsY + (_hdr ? 54 : 28);
        _defaultsExpanded = DrawFoldout(new Rect(10, foldoutY, width - 20, 20),
            _defaultsExpanded, "Defaults");
        var swatchesY = foldoutY + (_defaultsExpanded ? 48 : 22);
        if (_defaultsExpanded) DrawPalette(new Rect(12, foldoutY + 22, width - 24, 22), 7);
        _swatchesExpanded = DrawFoldout(new Rect(10, swatchesY, width - 20, 20),
            _swatchesExpanded, "Swatches");
        if (_swatchesExpanded) DrawPalette(new Rect(12, swatchesY + 22, width - 24, 46), 7);
    }

    private bool HandleWindowKeys()
    {
        if (Event.current.type != EventType.KeyDown) return false;
        if (Event.current.keyCode == KeyCode.Escape)
        {
            SetValue(_original);
            Event.current.Use();
            Close();
            return true;
        }
        if (Event.current.keyCode != KeyCode.Return) return false;
        Event.current.Use();
        Close();
        return true;
    }

    protected override void OnDisable() => EditorColorEyedropper.Cancel();

    private void DrawHeader(Rect rect)
    {
        var eyedropper = new Rect(rect.x, rect.y, 34, rect.height);
        var previews = new Rect(eyedropper.xMax + 6, rect.y,
            Fix64.Max(0, rect.width - eyedropper.width - 6), rect.height);
        var original = new Rect(previews.x, previews.y, previews.width / 2, previews.height);
        var current = new Rect(original.xMax, previews.y, previews.width - original.width, previews.height);
        if (GUI.Button(eyedropper, new GUIContent(string.Empty, tooltip: "Pick a color from the screen"),
                EditorStyles.colorField))
            EditorColorEyedropper.Begin(sampled =>
                SetValue(new Color(sampled.r, sampled.g, sampled.b, _value.a)));
        DrawEyedropper(eyedropper);
        if (GUI.Button(original, new GUIContent(string.Empty, tooltip: "Original color"),
                EditorStyles.colorPickerSwatch)) SetValue(_original);
        GUI.Label(current, new GUIContent(string.Empty, tooltip: "Current color"),
            EditorStyles.colorPickerSwatch);
        DrawTransparentSwatch(original, _original);
        DrawTransparentSwatch(current, _value);
    }

    private Color DrawChannels(Rect row, int hsvMode, Fix64 hue, Fix64 saturation, Fix64 brightness)
    {
        if (_mode == hsvMode)
        {
            var h = DrawHueChannel(row, "H", (float)(hue * 360), 0, 360);
            row = OffsetY(row, 24);
            var s = DrawChannel(row, "S", (float)saturation, 0, 1,
                Gray(brightness), EditorColorMath.HsvToRgb((Fix64)(h / 360), 1, brightness, 1));
            row = OffsetY(row, 24);
            var v = DrawChannel(row, "V", (float)brightness, 0, _hdr ? 8 : 1,
                Color.black, EditorColorMath.HsvToRgb((Fix64)(h / 360), (Fix64)s, 1, 1));
            var rgb = EditorColorMath.HsvToRgb((Fix64)(h / 360), (Fix64)s, (Fix64)v, _value.a);
            return DrawAlpha(row, rgb, integer: false);
        }

        var integer = _mode == 0;
        var multiplier = integer ? 255f : 1f;
        var maximum = _mode == 2 && _hdr ? 8f : multiplier;
        var red = (float)_value.r * multiplier;
        var green = (float)_value.g * multiplier;
        var blue = (float)_value.b * multiplier;
        red = DrawChannel(row, "R", red, 0, maximum,
            Preview(0, green / multiplier, blue / multiplier),
            Preview(maximum / multiplier, green / multiplier, blue / multiplier), integer);
        row = OffsetY(row, 24);
        green = DrawChannel(row, "G", green, 0, maximum,
            Preview(red / multiplier, 0, blue / multiplier),
            Preview(red / multiplier, maximum / multiplier, blue / multiplier), integer);
        row = OffsetY(row, 24);
        blue = DrawChannel(row, "B", blue, 0, maximum,
            Preview(red / multiplier, green / multiplier, 0),
            Preview(red / multiplier, green / multiplier, maximum / multiplier), integer);
        var rgbValue = new Color((Fix64)(red / multiplier), (Fix64)(green / multiplier),
            (Fix64)(blue / multiplier), _value.a);
        return DrawAlpha(row, rgbValue, integer);
    }

    private Color DrawAlpha(Rect rgbLastRow, Color color, bool integer)
    {
        if (!_showAlpha) return color;
        var row = new Rect(rgbLastRow.x, rgbLastRow.y + 24, rgbLastRow.width, rgbLastRow.height);
        var multiplier = integer ? 255f : 1f;
        var alpha = DrawChannel(row, "A", (float)color.a * multiplier, 0, multiplier,
            new Color(color.r, color.g, color.b, 0), new Color(color.r, color.g, color.b, 1), integer, true);
        return new Color(color.r, color.g, color.b, (Fix64)(alpha / multiplier));
    }

    private static float DrawChannel(Rect row, string label, float value, float minimum, float maximum,
        Color left, Color right, bool integer = false, bool checker = false)
    {
        GUI.Label(new Rect(row.x, row.y, 18, row.height), label);
        var numeric = new Rect(row.xMax - 62, row.y, 62, row.height);
        var slider = new Rect(row.x + 22, row.y + 3,
            Fix64.Max(0, numeric.x - row.x - 28), Fix64.Max(8, row.height - 6));
        if (checker) DrawCheckerboard(slider, 6);
        GUI.DrawGradientRect(slider, left, right, right, left);
        DrawBorder(slider, EditorAppearance.palette.Border);
        var normalized = maximum <= minimum ? Fix64.Zero : (Fix64)((value - minimum) / (maximum - minimum));
        if (HandleHorizontalValue(slider, ref normalized, $"ColorPickerChannel{label}"))
        {
            value = minimum + (float)normalized * (maximum - minimum);
            GUI.changed = true;
        }
        DrawSliderMarker(slider, normalized);
        var text = integer
            ? Math.Round(value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);
        text = GUI.TextField(numeric, text, style: EditorStyles.numberField);
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            value = Math.Clamp(parsed, minimum, maximum);
        return value;
    }

    private static float DrawHueChannel(Rect row, string label, float value, float minimum, float maximum)
    {
        GUI.Label(new Rect(row.x, row.y, 18, row.height), label);
        var numeric = new Rect(row.xMax - 62, row.y, 62, row.height);
        var slider = new Rect(row.x + 22, row.y + 3,
            Fix64.Max(0, numeric.x - row.x - 28), Fix64.Max(8, row.height - 6));
        DrawHueGradient(slider);
        DrawBorder(slider, EditorAppearance.palette.Border);
        var normalized = (Fix64)((value - minimum) / (maximum - minimum));
        if (HandleHorizontalValue(slider, ref normalized, "ColorPickerHueChannel"))
        {
            value = minimum + (float)normalized * (maximum - minimum);
            GUI.changed = true;
        }
        DrawSliderMarker(slider, normalized);
        var text = GUI.TextField(numeric, value.ToString("0.#", CultureInfo.InvariantCulture),
            style: EditorStyles.numberField);
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            value = Math.Clamp(parsed, minimum, maximum);
        return value;
    }

    private void DrawHexadecimal(Rect row)
    {
        GUI.Label(new Rect(row.x, row.y, 92, row.height), "Hexadecimal");
        var field = new Rect(row.x + 96, row.y, Fix64.Max(0, row.width - 96), row.height);
        GUI.SetNextControlName(HexControlName);
        var next = GUI.TextField(field, _hex, 8, EditorStyles.textField).Trim().TrimStart('#');
        if (string.Equals(next, _hex, StringComparison.Ordinal)) return;
        _hex = next.ToUpperInvariant();
        if (TryParseHex(_hex, _value.a, out var parsed)) SetValue(parsed);
    }

    private void DrawIntensity(Rect row)
    {
        var peak = Math.Max(.0001f, Math.Max((float)_value.r, Math.Max((float)_value.g, (float)_value.b)));
        var exposure = (float)Math.Log2(peak);
        GUI.Label(new Rect(row.x, row.y, 64, row.height), "Intensity", EditorStyles.miniLabel);
        var numeric = new Rect(row.xMax - 62, row.y, 62, row.height);
        var slider = new Rect(row.x + 68, row.y + 3,
            Fix64.Max(0, numeric.x - row.x - 74), Fix64.Max(8, row.height - 6));
        GUI.DrawGradientRect(slider, Color.black, Color.white, Color.white, Color.black);
        DrawBorder(slider, EditorAppearance.palette.Border);
        var normalized = (Fix64)((exposure + 10) / 20);
        EditorGUI.BeginChangeCheck();
        if (HandleHorizontalValue(slider, ref normalized, "ColorPickerIntensity")) GUI.changed = true;
        DrawSliderMarker(slider, normalized);
        var text = GUI.TextField(numeric, exposure.ToString("0.##", CultureInfo.InvariantCulture),
            style: EditorStyles.numberField);
        var next = float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, -10, 10)
            : exposure;
        if (GUI.changed && Math.Abs(next - exposure) < .0001f) next = (float)normalized * 20 - 10;
        if (EditorGUI.EndChangeCheck()) SetExposure(next);

        var buttonY = row.y + 24;
        var buttonWidth = Fix64.Min(42, (row.width - 16) / 5);
        for (var index = 0; index < 5; index++)
        {
            var offset = index - 2;
            var rect = new Rect(row.x + index * (buttonWidth + 4), buttonY, buttonWidth, 20);
            var label = offset > 0 ? $"+{offset}" : offset.ToString(CultureInfo.InvariantCulture);
            if (GUI.Button(rect, new GUIContent(label), EditorStyles.miniButton))
                SetExposure(exposure + offset);
        }
    }

    private void SetExposure(float exposure)
    {
        var peak = Math.Max(.0001f, Math.Max((float)_value.r, Math.Max((float)_value.g, (float)_value.b)));
        var scale = (float)Math.Pow(2, Math.Clamp(exposure, -10, 10)) / peak;
        SetValue(new Color((Fix64)((float)_value.r * scale), (Fix64)((float)_value.g * scale),
            (Fix64)((float)_value.b * scale), _value.a));
    }

    private static bool DrawFoldout(Rect rect, bool expanded, string label)
    {
        var icon = expanded ? EditorBuiltinIcons.Toolbar.FoldoutOpen : EditorBuiltinIcons.Toolbar.FoldoutClosed;
        if (GUI.Button(rect, new GUIContent(label, icon), EditorStyles.foldout)) expanded = !expanded;
        return expanded;
    }

    private void DrawPalette(Rect rect, int columns)
    {
        var gap = (Fix64)3;
        var rows = Math.Max(1, (int)Math.Ceiling(Palette.Length / (double)columns));
        var swatchWidth = (rect.width - gap * (columns - 1)) / columns;
        var swatchHeight = (rect.height - gap * (rows - 1)) / rows;
        for (var index = 0; index < Palette.Length; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var swatch = new Rect(rect.x + column * (swatchWidth + gap),
                rect.y + row * (swatchHeight + gap), swatchWidth, swatchHeight);
            if (GUI.Button(swatch, new GUIContent(string.Empty, tooltip: ToHex(Palette[index])),
                    EditorStyles.colorPickerSwatch))
            {
                var preset = Palette[index];
                SetValue(new Color(preset.r, preset.g, preset.b, _value.a));
            }
            GUI.DrawRect(new Rect(swatch.x + 1, swatch.y + 1, Fix64.Max(0, swatch.width - 2),
                Fix64.Max(0, swatch.height - 2)), Palette[index]);
            DrawBorder(swatch, EditorAppearance.palette.Border);
        }
    }

    private static void DrawSaturationValue(Rect rect, Fix64 hue, Fix64 saturation, Fix64 brightness)
    {
        var pure = EditorColorMath.HsvToRgb(hue, 1, 1, 1);
        GUI.DrawGradientRect(rect, Color.white, pure, Color.black, Color.black);
        DrawBorder(rect, EditorAppearance.palette.Border);
        var x = rect.x + rect.width * Fix64.Clamp(saturation, 0, 1);
        var y = rect.y + rect.height * (Fix64.One - Fix64.Clamp(brightness, 0, 1));
        DrawMarker(new Rect(x - 3, y - 3, 7, 7));
    }

    private static void DrawHue(Rect rect, Fix64 hue)
    {
        DrawHueGradient(rect);
        DrawBorder(rect, EditorAppearance.palette.Border);
        DrawSliderMarker(rect, hue);
    }

    private static void DrawHueGradient(Rect rect)
    {
        for (var segment = 0; segment < 6; segment++)
        {
            var left = EditorColorMath.HsvToRgb((Fix64)(segment / 6f), 1, 1, 1);
            var right = EditorColorMath.HsvToRgb((Fix64)((segment + 1) / 6f), 1, 1, 1);
            var x = rect.x + rect.width * segment / 6;
            var segmentRect = new Rect(x, rect.y, segment == 5 ? rect.xMax - x : rect.width / 6, rect.height);
            GUI.DrawGradientRect(segmentRect, left, right, right, left);
        }
    }

    private static void DrawTransparentSwatch(Rect rect, Color color)
    {
        var inner = new Rect(rect.x + 1, rect.y + 1, Fix64.Max(0, rect.width - 2),
            Fix64.Max(0, rect.height - 2));
        DrawCheckerboard(inner, 8);
        GUI.DrawRect(inner, color);
        DrawBorder(rect, EditorAppearance.palette.Border);
    }

    private static void DrawCheckerboard(Rect rect, Fix64 tile)
    {
        var columns = Math.Max(1, (int)Math.Ceiling((double)(rect.width / tile)));
        var rows = Math.Max(1, (int)Math.Ceiling((double)(rect.height / tile)));
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var shade = (row + column) % 2 == 0 ? Gray(Fix64.FromDecimal(.62m)) :
                Gray(Fix64.FromDecimal(.38m));
            GUI.DrawRect(new Rect(rect.x + column * tile, rect.y + row * tile,
                Fix64.Min(tile, rect.xMax - rect.x - column * tile),
                Fix64.Min(tile, rect.yMax - rect.y - row * tile)), shade);
        }
    }

    private static bool HandleSaturationValue(Rect rect, ref Fix64 saturation, ref Fix64 brightness)
    {
        var id = GUIUtility.GetControlID("ColorPickerSaturationValue".GetHashCode(StringComparison.Ordinal),
            FocusType.Passive, rect);
        var evt = Event.current;
        switch (evt.GetTypeForControl(id))
        {
            case EventType.MouseDown when evt.button == 0 && rect.Contains(evt.mousePosition):
                GUIUtility.hotControl = id;
                saturation = Fix64.Clamp((evt.mousePosition.x - rect.x) / Fix64.Max(1, rect.width), 0, 1);
                brightness = Fix64.One - Fix64.Clamp((evt.mousePosition.y - rect.y) /
                    Fix64.Max(1, rect.height), 0, 1);
                evt.Use();
                return true;
            case EventType.MouseDrag when GUIUtility.hotControl == id:
                saturation = Fix64.Clamp((evt.mousePosition.x - rect.x) / Fix64.Max(1, rect.width), 0, 1);
                brightness = Fix64.One - Fix64.Clamp((evt.mousePosition.y - rect.y) /
                    Fix64.Max(1, rect.height), 0, 1);
                evt.Use();
                return true;
            case EventType.MouseUp when GUIUtility.hotControl == id:
                GUIUtility.hotControl = 0;
                evt.Use();
                return false;
            default:
                GUI.AddCursorRect(rect, MouseCursor.SlideArrow);
                return false;
        }
    }

    private static bool HandleHorizontalValue(Rect rect, ref Fix64 value, string controlName)
    {
        var id = GUIUtility.GetControlID(controlName.GetHashCode(StringComparison.Ordinal), FocusType.Passive, rect);
        var evt = Event.current;
        switch (evt.GetTypeForControl(id))
        {
            case EventType.MouseDown when evt.button == 0 && rect.Contains(evt.mousePosition):
                GUIUtility.hotControl = id;
                value = Fix64.Clamp((evt.mousePosition.x - rect.x) /
                    Fix64.Max(1, rect.width), 0, 1);
                evt.Use();
                return true;
            case EventType.MouseDrag when GUIUtility.hotControl == id:
                value = Fix64.Clamp((evt.mousePosition.x - rect.x) /
                    Fix64.Max(1, rect.width), 0, 1);
                evt.Use();
                return true;
            case EventType.MouseUp when GUIUtility.hotControl == id:
                GUIUtility.hotControl = 0;
                evt.Use();
                return false;
            default:
                GUI.AddCursorRect(rect, MouseCursor.SlideArrow);
                return false;
        }
    }

    private void SetValue(Color value)
    {
        value = new Color(Fix64.Max(0, value.r), Fix64.Max(0, value.g), Fix64.Max(0, value.b),
            _showAlpha ? Fix64.Clamp(value.a, 0, 1) : _original.a);
        if (_value.Equals(value)) return;
        _value = value;
        if (!string.Equals(GUI.GetNameOfFocusedControl(), HexControlName, StringComparison.Ordinal))
            _hex = ToHex(value);
        if (_changed is { } changed)
            EditorFeatureGuard.Invoke(this, "ColorChanged", () => changed(value));
        Repaint();
    }

    private static void DrawSliderMarker(Rect rect, Fix64 normalized)
    {
        var x = rect.x + Fix64.Clamp(normalized, 0, 1) * rect.width;
        GUI.DrawRect(new Rect(x - 1, rect.y - 1, 3, rect.height + 2), Color.black);
        GUI.DrawRect(new Rect(x, rect.y, 1, rect.height), Color.white);
    }

    private static void DrawMarker(Rect rect)
    {
        DrawBorder(rect, Color.black);
        DrawBorder(new Rect(rect.x + 1, rect.y + 1, Fix64.Max(0, rect.width - 2),
            Fix64.Max(0, rect.height - 2)), Color.white);
    }

    private static void DrawEyedropper(Rect rect)
    {
        GUI.DrawRect(rect, rect.Contains(Event.current.mousePosition)
            ? EditorAppearance.palette.ButtonHover : EditorAppearance.palette.Button);
        DrawBorder(rect, EditorAppearance.palette.Border);
        var tint = GUI.enabled ? EditorAppearance.palette.Text : EditorAppearance.palette.DisabledText;
        var x = rect.x + (rect.width - 12) / 2;
        var y = rect.y + (rect.height - 12) / 2;
        for (var index = 0; index < 6; index++)
            GUI.DrawRect(new Rect(x + index, y + 8 - index, 2, 2), tint);
        GUI.DrawRect(new Rect(x + 7, y + 1, 4, 4), tint);
        GUI.DrawRect(new Rect(x + 1, y + 9, 3, 2), tint);
    }

    private static void DrawBorder(Rect rect, Color color)
    {
        GUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
        GUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
        GUI.DrawRect(new Rect(rect.x, rect.y + 1, 1, Fix64.Max(0, rect.height - 2)), color);
        GUI.DrawRect(new Rect(rect.xMax - 1, rect.y + 1, 1, Fix64.Max(0, rect.height - 2)), color);
    }

    private static Color Preview(float red, float green, float blue) =>
        EditorColorMath.OpaquePreview(new Color((Fix64)red, (Fix64)green, (Fix64)blue, 1));

    private static Rect OffsetY(Rect rect, Fix64 offset) =>
        new(rect.x, rect.y + offset, rect.width, rect.height);

    private static Color Gray(Fix64 value) => new(value, value, value, 1);

    private static Color Rgb(float red, float green, float blue) =>
        new((Fix64)red, (Fix64)green, (Fix64)blue, 1);

    private static string ToHex(Color color)
    {
        static byte Byte(Fix64 value) => (byte)Math.Clamp((int)Math.Round((double)value * 255), 0, 255);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Byte(color.r):X2}{Byte(color.g):X2}{Byte(color.b):X2}");
    }

    private static bool TryParseHex(string text, Fix64 alpha, out Color color)
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
