using BEngine.Editor.Diagnostics;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

/// <summary>Displays a selected render-event output without presenting it in the Game view.</summary>
internal sealed class FrameDebuggerPreviewView : IDisposable
{
    private const int ToolbarHeight = 24;
    private const int TabHeight = 22;
    private readonly string _registryKey = Guid.NewGuid().ToString("N");
    private FrameDebugPreviewSnapshot? _publishedSnapshot;
    private PreviewChannel _channel;
    private Fix64 _levels = Fix64.One;
    private long _revision;
    private string _imageSource = string.Empty;
    private PreviewChannel _publishedChannel;
    private Fix64 _publishedLevels;

    internal void Draw(Rect area, FrameDebugPreviewSnapshot? preview)
    {
        GUI.Box(area, GUIContent.none, EditorStyles.viewBackground);
        var showTabs = area.height >= 110;
        var toolbarY = area.y + 1;
        if (showTabs)
        {
            DrawTabs(new Rect(area.x + 1, toolbarY, Fix64.Max(1, area.width - 2), TabHeight));
            toolbarY += TabHeight;
        }
        var toolbar = new Rect(area.x + 1, toolbarY,
            Fix64.Max(1, area.width - 2), ToolbarHeight);
        DrawToolbar(toolbar);
        var output = new Rect(area.x + 1, toolbar.yMax,
            Fix64.Max(1, area.width - 2), Fix64.Max(1, area.yMax - toolbar.yMax - 1));
        GUI.DrawRect(output, new Color(
            Fix64.FromDecimal(0.035m), Fix64.FromDecimal(0.038m),
            Fix64.FromDecimal(0.043m), Fix64.One));

        if (preview is null)
        {
            DrawMessage(output, "Waiting for frame output...");
            return;
        }
        if (preview.Status is GraphicsColorReadbackStatus.Unavailable or
            GraphicsColorReadbackStatus.Failed)
        {
            DrawMessage(output, string.IsNullOrWhiteSpace(preview.Error)
                ? "Frame output readback is unavailable."
                : preview.Error);
            return;
        }
        if (preview.Status == GraphicsColorReadbackStatus.Pending || preview.Image is null)
        {
            DrawMessage(output, "Reading selected event output...");
            return;
        }

        PublishIfNeeded(preview);
        if (_imageSource.Length == 0)
        {
            DrawMessage(output, "Selected event output is empty.");
            return;
        }
        var image = preview.Image;
        var destination = FitAspect(output, image.Width, image.Height, 5);
        DrawCheckerboard(destination);
        GUI.DrawTexture(destination, _imageSource);
        GUI.DrawRect(new Rect(destination.x, destination.y, destination.width, 1),
            EditorStyles.separator.normal.backgroundColor);
        GUI.DrawRect(new Rect(destination.x, destination.yMax - 1, destination.width, 1),
            EditorStyles.separator.normal.backgroundColor);
        GUI.DrawRect(new Rect(destination.x, destination.y, 1, destination.height),
            EditorStyles.separator.normal.backgroundColor);
        GUI.DrawRect(new Rect(destination.xMax - 1, destination.y, 1, destination.height),
            EditorStyles.separator.normal.backgroundColor);
    }

    public void Dispose() => FrameDebuggerPreviewRegistry.Remove(_registryKey);

    private static void DrawTabs(Rect area)
    {
        GUI.Box(area, GUIContent.none, EditorStyles.contentToolbar);
        _ = GUI.Button(new Rect(area.x + 4, area.y + 1, 62, area.height - 2), "Output",
            EditorStyles.toolbarIconButtonSelected);
        using (new EditorGUI.DisabledScope(true))
            _ = GUI.Button(new Rect(area.x + 66, area.y + 1, 94, area.height - 2),
                new GUIContent("Mesh Preview", tooltip:
                    "Mesh preview requires captured vertex and index buffers."),
                EditorStyles.toolbarButton);
    }

    private void DrawToolbar(Rect area)
    {
        GUI.Box(area, GUIContent.none, EditorStyles.contentToolbar);
        var x = area.x + 4;
        using (new EditorGUI.DisabledScope(true))
            _ = GUI.Button(new Rect(x, area.y + 1, 68, area.height - 2), "RT 0",
                EditorStyles.toolbarPopup);
        x += 72;
        DrawChannelButton(ref x, area, PreviewChannel.Color, "RGBA", 46,
            EditorStyles.miniButtonLeft);
        DrawChannelButton(ref x, area, PreviewChannel.Red, "R", 27, EditorStyles.miniButtonMid);
        DrawChannelButton(ref x, area, PreviewChannel.Green, "G", 27, EditorStyles.miniButtonMid);
        DrawChannelButton(ref x, area, PreviewChannel.Blue, "B", 27, EditorStyles.miniButtonMid);
        DrawChannelButton(ref x, area, PreviewChannel.Alpha, "A", 27, EditorStyles.miniButtonRight);
        x += 8;
        if (area.xMax - x < 100) return;
        GUI.Label(new Rect(x, area.y + 1, 42, area.height - 2), "Levels", EditorStyles.miniLabel);
        x += 42;
        var sliderWidth = Fix64.Max(38, area.xMax - x - 36);
        var levels = GUI.HorizontalSlider(new Rect(x, area.y + 5, sliderWidth, 14),
            _levels, Fix64.FromDecimal(0.25m), Fix64.FromDecimal(2m));
        x += sliderWidth + 4;
        using (new EditorGUI.DisabledScope(true))
            _ = EditorGUI.FloatField(new Rect(x, area.y + 1, 32, area.height - 2), (float)levels);
        if (levels != _levels)
        {
            _levels = levels;
            _imageSource = string.Empty;
        }
    }

    private void DrawChannelButton(ref Fix64 x, Rect toolbar, PreviewChannel channel,
        string label, Fix64 width, GUIStyle style)
    {
        var selected = _channel == channel;
        var buttonStyle = selected ? EditorStyles.toolbarIconButtonSelected : style;
        if (GUI.Button(new Rect(x, toolbar.y + 1, width, toolbar.height - 2), label, buttonStyle) &&
            !selected)
        {
            _channel = channel;
            _imageSource = string.Empty;
        }
        x += width;
    }

    private void PublishIfNeeded(FrameDebugPreviewSnapshot preview)
    {
        if (ReferenceEquals(_publishedSnapshot, preview) && _publishedChannel == _channel &&
            _publishedLevels == _levels && _imageSource.Length > 0) return;
        if (preview.Image is not { } image) return;
        var pixels = _channel == PreviewChannel.Color && _levels == Fix64.One
            ? image.Pixels
            : ApplyDisplayTransform(image.Pixels.Span, _channel, (float)_levels);
        _imageSource = FrameDebuggerPreviewRegistry.Publish(
            _registryKey, image.Width, image.Height, pixels, ++_revision);
        _publishedSnapshot = preview;
        _publishedChannel = _channel;
        _publishedLevels = _levels;
    }

    private static ReadOnlyMemory<byte> ApplyDisplayTransform(ReadOnlySpan<byte> source,
        PreviewChannel channel, float levels)
    {
        var output = new byte[source.Length];
        for (var index = 0; index < source.Length; index += 4)
        {
            var red = Scale(source[index], levels);
            var green = Scale(source[index + 1], levels);
            var blue = Scale(source[index + 2], levels);
            var alpha = source[index + 3];
            switch (channel)
            {
                case PreviewChannel.Red:
                    output[index] = red;
                    output[index + 3] = byte.MaxValue;
                    break;
                case PreviewChannel.Green:
                    output[index + 1] = green;
                    output[index + 3] = byte.MaxValue;
                    break;
                case PreviewChannel.Blue:
                    output[index + 2] = blue;
                    output[index + 3] = byte.MaxValue;
                    break;
                case PreviewChannel.Alpha:
                    output[index] = alpha;
                    output[index + 1] = alpha;
                    output[index + 2] = alpha;
                    output[index + 3] = byte.MaxValue;
                    break;
                default:
                    output[index] = red;
                    output[index + 1] = green;
                    output[index + 2] = blue;
                    output[index + 3] = alpha;
                    break;
            }
        }
        return output;
    }

    private static byte Scale(byte value, float levels) =>
        (byte)Math.Clamp((int)MathF.Round(value * levels), 0, byte.MaxValue);

    private static Rect FitAspect(Rect bounds, int width, int height, Fix64 padding)
    {
        var availableWidth = Fix64.Max(1, bounds.width - padding * 2);
        var availableHeight = Fix64.Max(1, bounds.height - padding * 2);
        var scale = Fix64.Min(availableWidth / width, availableHeight / height);
        var fittedWidth = Fix64.Max(1, width * scale);
        var fittedHeight = Fix64.Max(1, height * scale);
        return new Rect(
            bounds.x + (bounds.width - fittedWidth) / 2,
            bounds.y + (bounds.height - fittedHeight) / 2,
            fittedWidth,
            fittedHeight);
    }

    private static void DrawCheckerboard(Rect area)
    {
        var tile = Fix64.Max(12, Fix64.Max(area.width / 20, area.height / 12));
        var dark = new Color(Fix64.FromDecimal(0.13m), Fix64.FromDecimal(0.13m),
            Fix64.FromDecimal(0.13m), Fix64.One);
        var light = new Color(Fix64.FromDecimal(0.22m), Fix64.FromDecimal(0.22m),
            Fix64.FromDecimal(0.22m), Fix64.One);
        var row = 0;
        for (var y = area.y; y < area.yMax; y += tile, row++)
        {
            var column = 0;
            for (var x = area.x; x < area.xMax; x += tile, column++)
                GUI.DrawRect(new Rect(x, y, Fix64.Min(tile, area.xMax - x),
                    Fix64.Min(tile, area.yMax - y)), ((row + column) & 1) == 0 ? dark : light);
        }
    }

    private static void DrawMessage(Rect area, string message)
    {
        GUI.Label(new Rect(area.x + 10, area.y + Fix64.Max(4, (area.height - 24) / 2),
            Fix64.Max(1, area.width - 20), 24), message, EditorStyles.centeredGreyMiniLabel);
    }

    private enum PreviewChannel
    {
        Color,
        Red,
        Green,
        Blue,
        Alpha
    }
}
