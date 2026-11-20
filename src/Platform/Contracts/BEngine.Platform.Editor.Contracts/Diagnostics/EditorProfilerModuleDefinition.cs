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
        bool __queryResult1059;
        {
            var __querySource1059 = Counters;
            ArgumentNullException.ThrowIfNull(__querySource1059, "source");
            __queryResult1059 = false;
            var __querySource1059Type = __querySource1059.GetType();
            if (__querySource1059Type == typeof(BEngine.Editor.EditorProfilerCounterDescriptor[]) || __querySource1059Type == typeof(System.Collections.Generic.List<BEngine.Editor.EditorProfilerCounterDescriptor>))
            {
                System.ReadOnlySpan<BEngine.Editor.EditorProfilerCounterDescriptor> __querySource1059Span = __querySource1059Type == typeof(BEngine.Editor.EditorProfilerCounterDescriptor[]) ? (BEngine.Editor.EditorProfilerCounterDescriptor[])(object)__querySource1059 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<BEngine.Editor.EditorProfilerCounterDescriptor>)(object)__querySource1059);
                foreach (var __queryItem1059 in __querySource1059Span)
                {
                    if ((string.IsNullOrWhiteSpace(__queryItem1059.Name) || string.IsNullOrWhiteSpace(__queryItem1059.DisplayName)))
                    {
                        __queryResult1059 = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var __queryItem1059 in __querySource1059)
                {
                    if ((string.IsNullOrWhiteSpace(__queryItem1059.Name) || string.IsNullOrWhiteSpace(__queryItem1059.DisplayName)))
                    {
                        __queryResult1059 = true;
                        break;
                    }
                }
            }
        }
        
        if (__queryResult1059)
            throw new ArgumentException("Profiler counter names cannot be empty.", nameof(counters));
        if (((((global::System.Collections.Generic.IEnumerable<string>)(Counters.Select(counter => counter.Name))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.Ordinal)) switch
        {
            (string[] { Length: 0 }, _) => 0,
            var (distinctItems2933, distinctComparer2933) => distinctItems2933.ToHashSet<string>(distinctComparer2933).Count
        }) !=
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
