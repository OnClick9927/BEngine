namespace BEngine.Editor;

public enum EditorProfilerCounterUnit
{
    Number,
    Milliseconds,
    Bytes,
    Percentage
}

public sealed record EditorProfilerCounterDescriptor(
    string Name,
    string DisplayName,
    EditorProfilerCounterUnit Unit,
    Color Color);

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

/// <summary>Global registry used to extend the Profiler module list from project and package code.</summary>
public static class EditorProfilerModuleRegistry
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, EditorProfilerModuleDefinition> Modules =
        new(StringComparer.Ordinal);

    public static event Action? modulesChanged;

    public static IDisposable Register(EditorProfilerModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        lock (Gate) Modules[module.Id] = module;
        EditorCallbackDispatcher.Invoke(modulesChanged, nameof(modulesChanged));
        return new Registration(module.Id, module);
    }

    public static bool Unregister(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        bool removed;
        lock (Gate) removed = Modules.Remove(id);
        if (removed) EditorCallbackDispatcher.Invoke(modulesChanged, nameof(modulesChanged));
        return removed;
    }

    public static EditorProfilerModuleDefinition[] GetModules()
    {
        lock (Gate) return Modules.Values.OrderBy(module => module.DisplayName,
            StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private sealed class Registration(
        string id,
        EditorProfilerModuleDefinition registeredModule) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            var removed = false;
            lock (Gate)
            {
                if (Modules.TryGetValue(id, out var current) &&
                    ReferenceEquals(current, registeredModule))
                    removed = Modules.Remove(id);
            }
            if (removed) EditorCallbackDispatcher.Invoke(modulesChanged, nameof(modulesChanged));
        }
    }
}
