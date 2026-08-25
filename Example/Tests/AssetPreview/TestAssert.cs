using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.AssetPreview;

internal static class TestAssert
{
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static GpuCanvasCommand Text(IEnumerable<GpuCanvasCommand> commands, string content) =>
        commands.FirstOrDefault(command => command.Type == GpuCanvasCommandType.Text &&
                                            command.Content == content) is
            { Type: GpuCanvasCommandType.Text } match
            ? match
            : throw new InvalidOperationException($"Inspector did not draw '{content}'.");

    internal static GpuCanvasCommand PreviewImage(IEnumerable<GpuCanvasCommand> commands, string path)
    {
        var match = commands.Where(command => command.Type == GpuCanvasCommandType.Image &&
                                               command.Content.StartsWith(path,
                                                   StringComparison.OrdinalIgnoreCase) &&
                                               command.Content.Contains("?bengine-preview=",
                                                   StringComparison.Ordinal))
            .OrderByDescending(command => command.Rect.Width * command.Rect.Height)
            .FirstOrDefault();
        return match.Type == GpuCanvasCommandType.Image
            ? match
            : throw new InvalidOperationException($"Inspector did not draw preview image '{path}'.");
    }
}
