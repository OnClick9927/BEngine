namespace BEngine.Editor;

/// <summary>
/// Describes a module contributed by engine, project, or package code. Counter values are submitted
/// through <see cref="EditorProfiler.ReportCounter"/> while a frame is being recorded.
/// </summary>
public sealed class EditorProfilerModuleDefinition
{
    public EditorProfilerModuleDefinition(
        string id,
        string displayName,
        IEnumerable<EditorProfilerCounterDescriptor> counters,
        string? icon = null,
        Action<Rect, EditorProfilerFrame>? drawDetails = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(counters);
        Id = id;
        DisplayName = displayName;
        Icon = icon ?? string.Empty;
        DrawDetails = drawDetails;
        Counters = counters.ToArray();
        if (Counters.Count == 0)
            throw new ArgumentException("A profiler module must define at least one counter.",
                nameof(counters));
        if (Counters.Any(counter => string.IsNullOrWhiteSpace(counter.Name) ||
                                    string.IsNullOrWhiteSpace(counter.DisplayName)))
            throw new ArgumentException("Profiler counter names cannot be empty.", nameof(counters));
        if (Counters.Select(counter => counter.Name).Distinct(StringComparer.Ordinal).Count() !=
            Counters.Count)
            throw new ArgumentException("Profiler counter names must be unique within a module.",
                nameof(counters));
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Icon { get; }
    public IReadOnlyList<EditorProfilerCounterDescriptor> Counters { get; }
    public Action<Rect, EditorProfilerFrame>? DrawDetails { get; }
}
