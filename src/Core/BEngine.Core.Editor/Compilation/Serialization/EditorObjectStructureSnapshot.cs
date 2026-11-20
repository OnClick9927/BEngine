namespace BEngine.Editor;

/// <summary>Retains scene membership, hierarchy order and component identity for an in-memory editor edit.</summary>
/// <remarks>No value capture, dirty marking, history or editor notifications are performed.</remarks>
public class EditorObjectStructureSnapshot
{
    public BObject Target { get; }
    private readonly Scene? _scene;
    private readonly GameObject[] _hierarchy;
    private readonly Dictionary<GameObject, Transform?> _parents;
    private readonly Dictionary<GameObject, int> _siblingIndices;
    private readonly int _componentIndex;

    protected EditorObjectStructureSnapshot(BObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Target = target;
        var root = target switch
        {
            GameObject gameObject => gameObject,
            Component component => component.gameObject,
            _ => null
        };
        _scene = root?.scene;
        _hierarchy = target is GameObject gameObjectTarget
            ? Enumerate(gameObjectTarget).ToArray()
            : [];
        _parents = _hierarchy.ToDictionary(static item => item,
            static item => item.transform.parent);
        _siblingIndices = _hierarchy.ToDictionary(static item => item,
            static item => item.transform.GetSiblingIndex());
        _componentIndex = target is Component targetComponent
            ? targetComponent.gameObject.ComponentIndex(targetComponent)
            : -1;
    }

    public static EditorObjectStructureSnapshot Capture(BObject target) => new(target);

    /// <summary>Queries structural removal without capturing or changing the object graph.
    /// Required-component rules are the same rules used when removing a component.</summary>
    public static bool CanRemove(BObject? target) => target is { isDestroyed: false } && (target switch
    {
        GameObject gameObject => gameObject.scene is { isCreated: true } scene && scene.Contains(gameObject),
        Component component => component.TryGetGameObject(out var owner) && owner.CanRemoveComponent(component),
        _ => false
    });

    public bool TryRemove(out Scene? affectedScene)
    {
        affectedScene = Target switch
        {
            GameObject owner => owner.scene,
            Component value => value.gameObject.scene,
            _ => _scene
        };
        var removed = Target switch
        {
            GameObject gameObject when gameObject.scene is { } scene => scene.Destroy(gameObject),
            Component component when component is not Transform =>
                component.gameObject.RemoveComponent(component),
            _ => false
        };
        return removed;
    }

    public virtual void Restore()
    {
        switch (Target)
        {
            case GameObject when _scene is not null:
                foreach (var gameObject in _hierarchy)
                    if (gameObject.scene is null) _scene.Add(gameObject);
                foreach (var gameObject in _hierarchy)
                    if (_parents[gameObject] is { } parent) gameObject.transform.SetParent(parent, false);
                foreach (var gameObject in _hierarchy)
                    gameObject.transform.SetSiblingIndex(_siblingIndices[gameObject]);
                break;
            case Component component when component is not Transform:
                component.gameObject.RestoreComponent(component, _componentIndex);
                break;
        }
    }

    private static IEnumerable<GameObject> Enumerate(GameObject root)
    {
        yield return root;
        foreach (var child in root.transform.children)
        foreach (var descendant in Enumerate(child.gameObject))
            yield return descendant;
    }
}
