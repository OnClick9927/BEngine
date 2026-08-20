using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.InspectorComponentActions;

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

    internal static bool HasText(IEnumerable<GpuCanvasCommand> commands, string content) =>
        commands.Any(command => command.Type == GpuCanvasCommandType.Text && command.Content == content);

    internal static GpuCanvasCommand ImageNear(IEnumerable<GpuCanvasCommand> commands, string content,
        float targetY) => commands
        .Where(command => command.Type == GpuCanvasCommandType.Image && command.Content == content)
        .OrderBy(command => Math.Abs(command.Rect.Y - targetY))
        .FirstOrDefault() is { Type: GpuCanvasCommandType.Image } match
            ? match
            : throw new InvalidOperationException($"Inspector did not draw image '{content}'.");
}
