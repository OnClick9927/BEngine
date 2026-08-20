namespace BEngine.Editor;

internal sealed class StructuralObjectState
{
    private readonly BObject _target;
    private readonly Scene? _scene;
    private readonly GameObject[] _hierarchy;
    private readonly Dictionary<GameObject, Transform?> _parents;
    private readonly Dictionary<GameObject, int> _siblingIndices;
    private readonly int _componentIndex;

    private StructuralObjectState(BObject target)
    {
        _target = target;
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

    internal static StructuralObjectState Capture(BObject target) => new(target);

    internal void Remove()
    {
        switch (_target)
        {
            case GameObject gameObject when gameObject.scene is { } scene:
                scene.Destroy(gameObject);
                break;
            case Component component when component is not Transform:
                if (!component.gameObject.RemoveComponent(component))
                    throw new InvalidOperationException(
                        $"{component.GetType().Name} cannot be removed because another component requires it.");
                break;
        }
    }

    internal void Restore()
    {
        switch (_target)
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
        EditorUtility.SetDirty(_target);
        EditorApplication.RaiseHierarchyChanged();
    }

    private static IEnumerable<GameObject> Enumerate(GameObject root)
    {
        yield return root;
        foreach (var child in root.transform.children)
        foreach (var descendant in Enumerate(child.gameObject))
            yield return descendant;
    }
}
