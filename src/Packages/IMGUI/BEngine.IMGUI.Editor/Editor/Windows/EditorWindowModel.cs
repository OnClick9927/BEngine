namespace BEngine.Editor;

/// <summary>Default per-instance model used by windows that do not require a specialized model.</summary>
public class EditorWindowModel(string windowId) : IEditorWindowModel
{
    private readonly Dictionary<string, object?> _state = new(StringComparer.Ordinal);

    public string WindowId { get; } = string.IsNullOrWhiteSpace(windowId)
        ? throw new ArgumentException("A window id is required.", nameof(windowId))
        : windowId;

    /// <summary>Extensible window state owned by the model rather than by static UI state.</summary>
    public IDictionary<string, object?> State => _state;
}
