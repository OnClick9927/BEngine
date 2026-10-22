namespace BEngine.Editor;

public sealed record NodeCanvasNode(
    string Id, string Title, Vector2 Position, Vector2 Size, Color32 Color,
    string Detail = "", bool Selected = false, Fix64 Progress = default)
{
    public IReadOnlyList<NodeCanvasPort> Inputs { get; init; } = [];
    public IReadOnlyList<NodeCanvasPort> Outputs { get; init; } = [];
}

public sealed record NodeCanvasPort(string Id, string Label, Color32 Color);

public sealed record NodeCanvasConnection(
    string Id, string SourceId, string DestinationId, Color32 Color, bool Selected = false,
    string? SourcePort = null, string? DestinationPort = null);
