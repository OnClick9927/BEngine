using System.Globalization;
using System.Reflection;
using BEngine;
using BEngine.Editor;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.ColorFieldUnityStyle;

internal static class Program
{
    private const int FieldWidth = 320;
    private const int PickerWidth = 360;
    private const int PickerHeight = 620;
    private static readonly Rect FieldRect = new(0, 0, FieldWidth, 18);
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod("BeginFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod("EndFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly Color Initial = new(Fix64.FromDecimal(0.125m), Fix64.FromDecimal(0.25m),
        Fix64.FromDecimal(0.5m), Fix64.FromDecimal(0.375m));
    private static Color _fieldValue = Initial;

    private static int Main()
    {
        try
        {
            VerifyTransparentAndOpaquePreviews();
            VerifyClickOpensAlphaPicker();
            VerifyNumericCommitAndCancel();
            VerifyHdrPicker();
            Console.WriteLine("COLOR_FIELD_UNITY_STYLE_OK|rgb-preview,alpha-bar,eyedropper,popup,checker,numeric-enter-escape,hdr-intensity");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            ReleaseFocusedWindow();
        }
    }

    private static void VerifyTransparentAndOpaquePreviews()
    {
        var commands = RenderField(showAlpha: true);
        var opaqueColor = new Color(Initial.r, Initial.g, Initial.b, 1);
        var opaquePreview = FindLargestColorRect(commands, GpuCanvasColor.FromColor(opaqueColor));
        Require(opaquePreview.Width > 100 && opaquePreview.Height > 8,
            "ColorField does not draw one readable, opaque RGB swatch.");

        var alphaBackground = commands.Where(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                                         command.Color == GpuCanvasColor.FromColor(Color.black) &&
                                                         command.Rect.Height is >= 2 and <= 5 &&
                                                         command.Rect.Width >= opaquePreview.Width - 1 &&
                                                         command.Rect.Y >= opaquePreview.Y)
            .OrderByDescending(command => command.Rect.Width)
            .FirstOrDefault();
        var alphaValue = commands.Where(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                                    command.Color == GpuCanvasColor.FromColor(Color.white) &&
                                                    command.Rect.Height is >= 2 and <= 5 &&
                                                    Math.Abs(command.Rect.Y - alphaBackground.Rect.Y) <= 0.1f)
            .OrderByDescending(command => command.Rect.Width)
            .FirstOrDefault();
        Require(alphaBackground.Rect.Width > 100 && alphaValue.Rect.Width > 0,
            "ColorField does not draw Unity's black/white Alpha bar along the RGB swatch bottom.");
        Require(Math.Abs(alphaValue.Rect.Width / alphaBackground.Rect.Width - (float)Initial.a) <= 0.03f,
            "ColorField Alpha bar does not encode the current Alpha value as its white width.");
        Require(commands.Where(command => command.Type == GpuCanvasCommandType.Text)
                .Select(command => command.Content).SequenceEqual(["Tint"]),
            "ColorField must render a compact swatch instead of inline RGBA channel text.");

        var withoutEyedropper = RenderField(showAlpha: true, showEyedropper: false);
        var withoutEyedropperPreview = FindLargestColorRect(withoutEyedropper,
            GpuCanvasColor.FromColor(opaqueColor));
        Require(commands.Count(command => command.Type == GpuCanvasCommandType.SolidRect &&
                    command.Rect.X >= 300 && command.Rect.Width <= 4) >
                withoutEyedropper.Count(command => command.Type == GpuCanvasCommandType.SolidRect &&
                    command.Rect.X >= 300 && command.Rect.Width <= 4),
            "ColorField ignores showEyedropper instead of drawing a dedicated eyedropper glyph.");
        Require(withoutEyedropperPreview.Width >= opaquePreview.Width + 18,
            "Hiding the 20px eyedropper region did not return its width to the color swatch.");
        DispatchField(new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(310, 9), button = 0
        }, showAlpha: true);
        DispatchField(new Event(EventType.MouseUp)
        {
            mousePosition = new Vector2(310, 9), button = 0
        }, showAlpha: true);
        var eyedropperType = typeof(EditorGUI).Assembly.GetType("BEngine.Editor.EditorColorEyedropper", true)!;
        var active = eyedropperType.GetProperty("active", BindingFlags.Static | BindingFlags.NonPublic)!;
        Require((bool)active.GetValue(null)!, "Clicking the eyedropper region did not arm screen sampling.");
        eyedropperType.GetMethod("Cancel", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);

        var rgbCommands = RenderField(showAlpha: false);
        var rgbPreview = FindLargestColorRect(rgbCommands, GpuCanvasColor.FromColor(opaqueColor));
        Require(rgbPreview.Width >= opaquePreview.Width - 1 && rgbPreview.Height >= opaquePreview.Height - 1,
            "A ColorField without Alpha does not retain its full opaque RGB swatch.");
        Require(!rgbCommands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                            command.Color == GpuCanvasColor.FromColor(Color.white) &&
                                            command.Rect.Height is >= 2 and <= 5 &&
                                            command.Rect.Width < rgbPreview.Width - 1 &&
                                            command.Rect.Y >= rgbPreview.Y),
            "A ColorField without Alpha still draws an Alpha value bar.");
    }

    private static void VerifyClickOpensAlphaPicker()
    {
        _fieldValue = Initial;
        OpenPicker();

        var picker = RequirePicker();
        Require(picker.titleContent.text == "Color", "ColorField popup does not use Unity's 'Color' title.");
        var commands = RenderPicker(picker);
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                        command.Content is "Alpha" or "A"),
            "The Color picker opened by an RGBA field has no Alpha control.");
        Require(commands.Count(command => command.Type == GpuCanvasCommandType.SolidRect &&
                    command.Rect.Y is >= 10 and <= 44 && command.Rect.Width > 80) >= 2,
            "The Color picker does not expose Unity-style Original and Current swatches.");
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                        command.Content == "Hexadecimal"),
            "The Color picker does not label its hexadecimal input.");
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text && IsHexColor(command.Content)),
            "The Color picker does not expose an RGB/RGBA hexadecimal value.");

        var checkerColors = commands.Where(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                                       command.Color.A == byte.MaxValue &&
                                                       command.Color.R == command.Color.G &&
                                                       command.Color.G == command.Color.B &&
                                                       command.Color.R is >= 80 and <= 180 &&
                                                       command.Rect.Width is >= 4 and <= 12 &&
                                                       command.Rect.Height is >= 4 and <= 12)
            .Select(command => command.Color).Distinct().ToArray();
        Require(checkerColors.Length >= 2,
            "The Color picker preview does not use a two-tone transparency checkerboard.");
    }

    private static void VerifyNumericCommitAndCancel()
    {
        var picker = RequirePicker();
        var initialCommands = RenderPicker(picker);
        var alphaInput = FindChannelInput(initialCommands, "Alpha", "A");
        var byteMode = ParseNumber(alphaInput.Content) > 1;
        var committedText = byteMode ? "191" : "0.75";
        var committedAlpha = byteMode ? 191f / 255f : 0.75f;

        ReplaceText(picker, alphaInput.Rect, committedText);
        DispatchPicker(picker, Event.KeyboardEvent("enter"));
        DispatchField(new Event(EventType.Layout), showAlpha: true);
        Require(Approximately(_fieldValue.a, committedAlpha),
            $"Committing the Alpha input did not update ColorField: {_fieldValue.a}.");

        OpenPicker();
        picker = RequirePicker();
        var committedCommands = RenderPicker(picker);
        alphaInput = FindChannelInput(committedCommands, "Alpha", "A");
        var cancelledText = byteMode ? "51" : "0.2";
        var cancelledAlpha = byteMode ? 51f / 255f : 0.2f;
        ReplaceText(picker, alphaInput.Rect, cancelledText);
        DispatchField(new Event(EventType.Layout), showAlpha: true);
        Require(Approximately(_fieldValue.a, cancelledAlpha),
            "The Color picker did not live-update the numeric Alpha edit.");

        DispatchPicker(picker, Event.KeyboardEvent("escape"));
        DispatchField(new Event(EventType.Layout), showAlpha: true);
        Require(Approximately(_fieldValue.a, committedAlpha),
            $"Escape did not cancel edits and restore the color captured when the picker opened: {_fieldValue}.");
    }

    private static void VerifyHdrPicker()
    {
        _fieldValue = new Color((Fix64)2, Fix64.One, Fix64.FromDecimal(.5m), Fix64.One);
        var fieldCommands = RenderField(showAlpha: true, hdr: true);
        Require(fieldCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                             command.Content == "HDR"),
            "An HDR ColorField does not identify an overbright value.");
        DispatchField(new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(220, 9), button = 0
        }, showAlpha: true, hdr: true);
        DispatchField(new Event(EventType.MouseUp)
        {
            mousePosition = new Vector2(220, 9), button = 0
        }, showAlpha: true, hdr: true);
        var picker = RequirePicker();
        Require(picker.titleContent.text == "HDR Color", "HDR ColorField opened a non-HDR picker.");
        var commands = RenderPicker(picker);
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                        command.Content == "Intensity"),
            "HDR Color picker does not expose Unity-style Intensity controls.");
        picker.Close();
    }

    private static GpuCanvasCommand FindChannelInput(IReadOnlyList<GpuCanvasCommand> commands,
        params string[] channels)
    {
        var label = commands.Single(command => command.Type == GpuCanvasCommandType.Text &&
                                               channels.Contains(command.Content, StringComparer.Ordinal));
        var centerY = label.Rect.Y + label.Rect.Height / 2;
        var input = commands.Where(command => command.Type == GpuCanvasCommandType.Text &&
                                              command.Rect.X > label.Rect.X &&
                                              Math.Abs(command.Rect.Y + command.Rect.Height / 2 - centerY) <= 1 &&
                                              float.TryParse(command.Content, NumberStyles.Float,
                                                  CultureInfo.InvariantCulture, out _))
            .OrderByDescending(command => command.Rect.X)
            .FirstOrDefault();
        Require(input.Rect.Width > 0, $"The {string.Join('/', channels)} row has no editable numeric input.");
        return input;
    }

    private static void OpenPicker()
    {
        DispatchField(new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(220, 9), button = 0, clickCount = 1
        }, showAlpha: true);
        DispatchField(new Event(EventType.MouseUp)
        {
            mousePosition = new Vector2(220, 9), button = 0, clickCount = 1
        }, showAlpha: true);
    }

    private static void ReplaceText(EditorWindow picker, GpuCanvasRect inputRect, string replacement)
    {
        var pointer = new Vector2((Fix64)(inputRect.X + inputRect.Width / 2),
            (Fix64)(inputRect.Y + inputRect.Height / 2));
        DispatchPicker(picker, new Event(EventType.MouseDown)
        {
            mousePosition = pointer, button = 0, clickCount = 2
        });
        DispatchPicker(picker, new Event(EventType.MouseUp)
        {
            mousePosition = pointer, button = 0, clickCount = 2
        });
        foreach (var character in replacement)
            DispatchPicker(picker, new Event(EventType.KeyDown) { character = character });
    }

    private static GpuCanvasRect FindLargestColorRect(IEnumerable<GpuCanvasCommand> commands,
        GpuCanvasColor color) => commands
        .Where(command => command.Type == GpuCanvasCommandType.SolidRect && command.Color == color)
        .OrderByDescending(command => command.Rect.Width * command.Rect.Height)
        .Select(command => command.Rect)
        .FirstOrDefault();

    private static List<GpuCanvasCommand> RenderField(bool showAlpha, bool showEyedropper = true,
        bool hdr = false)
    {
        var commands = new List<GpuCanvasCommand>();
        DispatchField(new Event(EventType.Repaint), showAlpha, commands, showEyedropper, hdr);
        return commands;
    }

    private static void DispatchField(Event evt, bool showAlpha,
        List<GpuCanvasCommand>? commands = null, bool showEyedropper = true, bool hdr = false)
    {
        Dispatch(evt, FieldWidth, 40,
            () => _fieldValue = EditorGUI.ColorField(FieldRect, "Tint", _fieldValue,
                showEyedropper: showEyedropper, showAlpha: showAlpha, hdr: hdr), commands);
    }

    private static List<GpuCanvasCommand> RenderPicker(EditorWindow picker)
    {
        var commands = new List<GpuCanvasCommand>();
        DispatchPicker(picker, new Event(EventType.Repaint), commands);
        return commands;
    }

    private static void DispatchPicker(EditorWindow picker, Event evt,
        List<GpuCanvasCommand>? commands = null)
    {
        var onGui = picker.GetType().GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.NonPublic) ??
                    throw new InvalidOperationException("EditorColorPickerWindow.OnGUI was not found.");
        Dispatch(evt, PickerWidth, PickerHeight, () => onGui.Invoke(picker, null), commands);
    }

    private static void Dispatch(Event evt, int width, int height, Action draw,
        List<GpuCanvasCommand>? commands = null)
    {
        BeginFrame.Invoke(null, [evt, width, height, commands ?? []]);
        try { draw(); }
        finally { EndFrame.Invoke(null, null); }
    }

    private static EditorWindow RequirePicker()
    {
        var picker = EditorWindow.focusedWindow;
        Require(picker?.GetType().Name == "EditorColorPickerWindow",
            "Clicking the ColorField swatch did not open EditorColorPickerWindow.");
        return picker!;
    }

    private static void ReleaseFocusedWindow()
    {
        var picker = EditorWindow.focusedWindow;
        if (picker is null) return;
        typeof(EditorWindow).GetMethod("LoseFocusInternal", BindingFlags.Instance | BindingFlags.NonPublic)?
            .Invoke(picker, null);
    }

    private static float ParseNumber(string value) =>
        float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static bool IsHexColor(string value)
    {
        var digits = value.Trim().TrimStart('#');
        return digits.Length is 6 or 8 && digits.All(Uri.IsHexDigit);
    }

    private static bool Approximately(Fix64 actual, float expected) =>
        Math.Abs((float)actual - expected) <= 0.01f;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
