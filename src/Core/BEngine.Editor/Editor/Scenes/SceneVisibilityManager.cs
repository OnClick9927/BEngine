namespace BEngine.Editor;

/// <summary>
/// Stores Scene view visibility and picking overrides without changing GameObject runtime state.
/// </summary>
public sealed class SceneVisibilityManager
{
    private readonly HashSet<SceneObjectKey> _hidden = [];
    private readonly HashSet<SceneObjectKey> _pickingDisabled = [];

    public static SceneVisibilityManager instance { get; } = new();

    public event Action? visibilityChanged;
    public event Action? pickingChanged;

    private SceneVisibilityManager() { }

    public bool IsHidden(GameObject gameObject)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        return _hidden.Contains(Key(gameObject));
    }

    public bool IsVisible(GameObject gameObject) => !IsHidden(gameObject);

    public void Hide(GameObject gameObject, bool includeDescendants = true) =>
        SetVisibility(gameObject, visible: false, includeDescendants);

    public void Show(GameObject gameObject, bool includeDescendants = true) =>
        SetVisibility(gameObject, visible: true, includeDescendants);

    public void ToggleVisibility(GameObject gameObject, bool includeDescendants = false)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        SetVisibility(gameObject, IsHidden(gameObject), includeDescendants);
    }

    public bool IsPickingDisabled(GameObject gameObject)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        return _pickingDisabled.Contains(Key(gameObject));
    }

    public void DisablePicking(GameObject gameObject, bool includeDescendants = true) =>
        SetPicking(gameObject, enabled: false, includeDescendants);

    public void EnablePicking(GameObject gameObject, bool includeDescendants = true) =>
        SetPicking(gameObject, enabled: true, includeDescendants);

    public void TogglePicking(GameObject gameObject, bool includeDescendants = false)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        SetPicking(gameObject, IsPickingDisabled(gameObject), includeDescendants);
    }

    public void ShowAll()
    {
        if (_hidden.Count == 0) return;
        _hidden.Clear();
        NotifyVisibilityChanged();
    }

    public void EnableAllPicking()
    {
        if (_pickingDisabled.Count == 0) return;
        _pickingDisabled.Clear();
        NotifyPickingChanged();
    }

    private void SetVisibility(GameObject gameObject, bool visible, bool includeDescendants)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        var changed = false;
        foreach (var target in Enumerate(gameObject, includeDescendants))
            changed |= visible ? _hidden.Remove(Key(target)) : _hidden.Add(Key(target));
        if (changed) NotifyVisibilityChanged();
    }

    private void SetPicking(GameObject gameObject, bool enabled, bool includeDescendants)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        var changed = false;
        foreach (var target in Enumerate(gameObject, includeDescendants))
            changed |= enabled
                ? _pickingDisabled.Remove(Key(target))
                : _pickingDisabled.Add(Key(target));
        if (changed) NotifyPickingChanged();
    }

    private void NotifyVisibilityChanged()
    {
        EditorCallbackDispatcher.Invoke(visibilityChanged, nameof(visibilityChanged));
        SceneView.RepaintAll();
    }

    private void NotifyPickingChanged()
    {
        EditorCallbackDispatcher.Invoke(pickingChanged, nameof(pickingChanged));
        SceneView.RepaintAll();
    }

    private static IEnumerable<GameObject> Enumerate(GameObject gameObject, bool includeDescendants)
    {
        yield return gameObject;
        if (!includeDescendants) yield break;
        foreach (var child in gameObject.transform.children)
        foreach (var descendant in Enumerate(child.gameObject, true))
            yield return descendant;
    }

    private static SceneObjectKey Key(GameObject gameObject) =>
        new(gameObject.scene?.Id ?? Guid.Empty, gameObject.Id);

    private readonly record struct SceneObjectKey(Guid SceneId, Guid GameObjectId);
}
