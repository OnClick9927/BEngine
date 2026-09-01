using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Diagnostics;

/// <summary>Publishes the selected Frame Debugger step over its source Game view.</summary>
internal sealed class FrameDebuggerGamePreviewView : IDisposable
{
    private readonly string _registryKey = Guid.NewGuid().ToString("N");
    private FrameDebugPreviewSnapshot? _publishedSnapshot;
    private string _imageSource = string.Empty;
    private long _revision;

    internal void Draw(Rect area, FrameDebugPreviewSnapshot? preview, int expectedEventCount)
    {
        GUI.DrawRect(area, Color.black);
        if (preview is null || preview.EventCount != expectedEventCount)
        {
            DrawMessage(area, "Rendering selected Frame Debugger step...");
            return;
        }
        if (preview.Status is GraphicsColorReadbackStatus.Unavailable or
            GraphicsColorReadbackStatus.Failed)
        {
            DrawMessage(area, string.IsNullOrWhiteSpace(preview.Error)
                ? "Selected step output is unavailable."
                : preview.Error);
            return;
        }
        if (preview.Status == GraphicsColorReadbackStatus.Pending || preview.Image is null)
        {
            DrawMessage(area, "Reading selected Frame Debugger step...");
            return;
        }

        PublishIfNeeded(preview);
        if (_imageSource.Length == 0) return;
        var image = preview.Image;
        GUI.DrawTexture(FitAspect(area, image.Width, image.Height), _imageSource);
    }

    internal void Clear()
    {
        if (_publishedSnapshot is null && _imageSource.Length == 0) return;
        FrameDebuggerPreviewRegistry.Remove(_registryKey);
        _publishedSnapshot = null;
        _imageSource = string.Empty;
    }

    public void Dispose() => Clear();

    private void PublishIfNeeded(FrameDebugPreviewSnapshot preview)
    {
        if (ReferenceEquals(_publishedSnapshot, preview) && _imageSource.Length > 0) return;
        if (preview.Image is not { } image) return;
        _imageSource = FrameDebuggerPreviewRegistry.Publish(
            _registryKey, image.Width, image.Height, image.Pixels, ++_revision);
        _publishedSnapshot = preview;
    }

    private static Rect FitAspect(Rect bounds, int width, int height)
    {
        var scale = Fix64.Min(bounds.width / width, bounds.height / height);
        var fittedWidth = Fix64.Max(1, width * scale);
        var fittedHeight = Fix64.Max(1, height * scale);
        return new Rect(
            bounds.x + (bounds.width - fittedWidth) / 2,
            bounds.y + (bounds.height - fittedHeight) / 2,
            fittedWidth,
            fittedHeight);
    }

    private static void DrawMessage(Rect area, string message) =>
        GUI.Label(new Rect(area.x + 10, area.y + Fix64.Max(4, (area.height - 24) / 2),
            Fix64.Max(1, area.width - 20), 24), message, EditorStyles.centeredGreyMiniLabel);
}
