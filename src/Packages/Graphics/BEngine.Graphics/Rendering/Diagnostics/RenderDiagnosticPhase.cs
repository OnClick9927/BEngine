namespace BEngine.Rendering;

/// <summary>A phase identity owned by the rendering implementation that measures it.
/// Equal names merge into the same row within a capture. A default value is disabled.</summary>
public readonly record struct RenderDiagnosticPhase
{
    public RenderDiagnosticPhase(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }
    public string? Name { get; }
}
