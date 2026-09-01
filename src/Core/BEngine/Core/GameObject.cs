using System.Runtime.InteropServices;

namespace BEngine;

public class GameObject : BObject
{
    private readonly List<Component> _components = [];
    private readonly IReadOnlyList<Component> _componentsView;
    private bool _activeSelf = true;
    private bool _activeInHierarchy = true;
    private bool _dontDestroyOnLoad;
    private string _tag = "Untagged";
    private ulong _layer = SortingLayer.Default;
    private bool _isStatic;
    private Transform _transform;
    private Scene? _scene;

    public bool activeSelf
    {
        get { return _activeSelf; }
        set
        {
            if (_activeSelf == value) return;
            _activeSelf = value;
            SynchronizeActiveStateHierarchy();
            SceneRuntime.NotifyHierarchyStateChanged(this);
        }
    }
    public bool activeInHierarchy
    {
        get { return ActiveInHierarchyUnchecked; }
    }
    public string tag
    {
        get { return _tag; }
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            var normalized = value.Trim();
            if (!TagManager.IsDefined(normalized))
                throw new ArgumentException($"Tag '{normalized}' is not defined.", nameof(value));
            _tag = normalized;
        }
    }
    public ulong layer
    {
        get { return _layer; }
        set
        {
            SortingLayer.Validate(value);
            _layer = value;
        }
    }
    public bool isStatic
    {
        get { return _isStatic; }
        set
        {
            _isStatic = value;
        }
    }
    public bool isDontDestroyOnLoad
    {
        get { return _transform.RootUnchecked.GameObjectUnchecked._dontDestroyOnLoad; }
    }
    public Transform transform
    {
        get { return _transform; }
        private set => _transform = value;
    }
    public Scene? scene
    {
        get { return _scene; }
        internal set => _scene = value;
    }
    public IReadOnlyList<Component> components
    {
        get { return _componentsView; }
    }

    internal bool ActiveInHierarchyUnchecked => _activeInHierarchy;
    internal Transform TransformUnchecked => _transform;
    internal Scene? SceneUnchecked => _scene;
    internal IReadOnlyList<Component> ComponentsUnchecked => _components;
    internal ReadOnlySpan<Component> ComponentsSpanUnchecked => CollectionsMarshal.AsSpan(_components);

    public GameObject(string name = "GameObject")
    {
        _componentsView = _components.AsReadOnly();
        this.name = name;
        _transform = new Transform();
        Attach(_transform);
    }

    public T AddComponent<T>() where T : Component, new() => (T)AddComponent(typeof(T));

    public Component AddComponent(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        if (!typeof(Component).IsAssignableFrom(componentType) || componentType.IsAbstract)
        {
            throw new ArgumentException($"{componentType.FullName} is not a concrete Component type.", nameof(componentType));
        }

        if (typeof(Transform).IsAssignableFrom(componentType))
        {
            if (componentType == typeof(Transform) || _transform.GetType() != typeof(Transform))
            {
                throw new InvalidOperationException("Every GameObject owns exactly one Transform.");
            }

            if (!RuntimeTypeCache.TryCreateInstance(componentType, out var createdTransform) ||
                createdTransform is not Transform replacement)
                throw new InvalidOperationException($"Unable to create {componentType.FullName}.");
            replacement.Id = _transform.Id;
            replacement.IsRuntimeOnly = IsRuntimeOnly;
            replacement.Attach(this);
            _transform.TransferTo(replacement);
            var transformIndex = _components.IndexOf(_transform);
            _components[transformIndex] = replacement;
            _transform = replacement;
            return replacement;
        }

        var reflection = RuntimeTypeCache.GetComponentInfo(componentType);
        if (reflection.DisallowMultiple &&
            _components.Any(componentType.IsInstanceOfType))
        {
            throw new InvalidOperationException($"{componentType.Name} cannot be added more than once.");
        }

        foreach (var requiredType in reflection.RequiredComponents)
        {
            if (GetComponent(requiredType) is null) AddComponent(requiredType);
        }

        if (reflection.Factory is null)
            throw new InvalidOperationException($"Unable to create {componentType.FullName}.");
        var component = reflection.Factory();
        Attach(component);
        return component;
    }

    internal Component Attach(Component component)
    {
        component.IsRuntimeOnly = IsRuntimeOnly;
        component.Attach(this);
        _components.Add(component);
        SceneRuntime.NotifyComponentStateChanged(component);
        return component;
    }

    public T? GetComponent<T>() where T : Component
    {
        return GetComponentUnchecked<T>();
    }

    public Component? GetComponent(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return GetComponentUnchecked(type);
    }

    public IReadOnlyList<T> GetComponents<T>() where T : Component
    {
        return GetComponentsUnchecked<T>();
    }

    internal T? GetComponentUnchecked<T>() where T : Component
    {
        foreach (var component in CollectionsMarshal.AsSpan(_components))
            if (component is T typed) return typed;
        return null;
    }

    internal Component? GetComponentUnchecked(Type type)
    {
        foreach (var component in CollectionsMarshal.AsSpan(_components))
            if (type.IsInstanceOfType(component)) return component;
        return null;
    }

    internal T[] GetComponentsUnchecked<T>() where T : Component
    {
        var components = CollectionsMarshal.AsSpan(_components);
        var count = 0;
        foreach (var component in components)
            if (component is T) count++;
        if (count == 0) return [];

        var result = new T[count];
        var index = 0;
        foreach (var component in components)
            if (component is T typed) result[index++] = typed;
        return result;
    }

    internal void AddComponentsUnchecked<T>(List<T> destination) where T : Component
    {
        foreach (var component in CollectionsMarshal.AsSpan(_components))
            if (component is T typed) destination.Add(typed);
    }

    public bool TryGetComponent<T>(out T? component) where T : Component
    {
        component = GetComponentUnchecked<T>();
        return component is not null;
    }

    public T? GetComponentInParent<T>() where T : Component
    {
        for (var current = _transform; current is not null; current = current.ParentUnchecked)
        {
            if (current.GameObjectUnchecked.GetComponentUnchecked<T>() is { } component) return component;
        }
        return null;
    }

    public T? GetComponentInChildren<T>(bool includeInactive = false) where T : Component
    {
        return GetComponentInChildrenUnchecked<T>(includeInactive);
    }

    public IReadOnlyList<T> GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
    {
        return GetComponentsInChildrenUnchecked<T>(includeInactive);
    }

    internal IReadOnlyList<T> GetComponentsInChildrenUnchecked<T>(bool includeInactive) where T : Component
    {
        var result = new List<T>();
        CollectComponentsInChildren(this, includeInactive, result);
        return result;
    }

    public IReadOnlyList<T> GetComponentsInParent<T>(bool includeInactive = false) where T : Component
    {
        return GetComponentsInParentUnchecked<T>(includeInactive);
    }

    internal IReadOnlyList<T> GetComponentsInParentUnchecked<T>(bool includeInactive) where T : Component
    {
        var result = new List<T>();
        for (var current = _transform; current is not null; current = current.ParentUnchecked)
        {
            var owner = current.GameObjectUnchecked;
            if (includeInactive || owner.ActiveInHierarchyUnchecked)
                owner.AddComponentsUnchecked(result);
        }
        return result;
    }

    public static GameObject? Find(string name)
    {
        return FindObjectsByType<GameObject>()
            .FirstOrDefault(item => item.ActiveInHierarchyUnchecked && item.name == name);
    }

    public static GameObject? FindWithTag(string tag)
    {
        return FindGameObjectsWithTag(tag).FirstOrDefault();
    }

    public static GameObject[] FindGameObjectsWithTag(string tag)
    {
        if (!TagManager.IsDefined(tag))
            throw new ArgumentException($"Tag '{tag}' is not defined.", nameof(tag));
        var normalized = tag.Trim();
        return FindObjectsByType<GameObject>()
            .Where(item => item.ActiveInHierarchyUnchecked && item.CompareTagUnchecked(normalized)).ToArray();
    }

    public void SetActive(bool value) => activeSelf = value;
    public bool CompareTag(string value)
    {
        return CompareTagUnchecked(value);
    }

    internal bool CompareTagUnchecked(string value)
    {
        if (!TagManager.IsDefined(value))
            throw new ArgumentException($"Tag '{value}' is not defined.", nameof(value));
        return string.Equals(_tag, value.Trim(), StringComparison.Ordinal);
    }

    public bool RemoveComponent(Component component)
    {
        if (!CanRemoveComponent(component)) return false;
        SceneRuntime.NotifyComponentDestroying(component);
        return _components.Remove(component);
    }

    internal bool CanRemoveComponent(Component component)
    {
        if (component is null || ReferenceEquals(component, _transform) || !_components.Contains(component))
            return false;
        foreach (var dependent in _components)
        {
            if (ReferenceEquals(dependent, component)) continue;
            foreach (var requiredType in RuntimeTypeCache.GetComponentInfo(dependent.GetType()).RequiredComponents)
            {
                var requirementWasSatisfied = _components.Any(requiredType.IsInstanceOfType);
                var requirementRemainsSatisfied = _components.Any(candidate =>
                    !ReferenceEquals(candidate, component) && requiredType.IsInstanceOfType(candidate));
                if (requirementWasSatisfied && !requirementRemainsSatisfied) return false;
            }
        }
        return true;
    }

    internal int ComponentIndex(Component component) => _components.IndexOf(component);

    internal bool RestoreComponent(Component component, int index)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (_components.Contains(component) || !ReferenceEquals(component.gameObject, this)) return false;
        _components.Insert(Math.Clamp(index, 1, _components.Count), component);
        SceneRuntime.NotifyComponentStateChanged(component);
        return true;
    }

    internal void BindToScene(Scene owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_scene is not null)
            throw new InvalidOperationException("GameObject already belongs to a Scene.");
        _scene = owner;
    }

    internal void UnbindFromScene()
    {
        _scene = null;
    }

    internal void RestoreSceneBinding(Scene owner) => _scene = owner;

    internal void MarkDontDestroyOnLoad() => _transform.RootUnchecked.GameObjectUnchecked._dontDestroyOnLoad = true;

    internal void ClearDontDestroyOnLoad() => _transform.RootUnchecked.GameObjectUnchecked._dontDestroyOnLoad = false;

    internal void SynchronizeActiveStateHierarchy()
    {
        var parent = _transform.ParentUnchecked;
        var activeInHierarchy = _activeSelf &&
                                (parent is null || parent.GameObjectUnchecked._activeInHierarchy);
        if (_activeInHierarchy == activeInHierarchy) return;
        _activeInHierarchy = activeInHierarchy;
        foreach (var child in _transform.ChildrenUnchecked)
            child.GameObjectUnchecked.SynchronizeActiveStateHierarchy();
    }

    private static void CollectComponentsInChildren<T>(GameObject current, bool includeInactive, List<T> result)
        where T : Component
    {
        if (includeInactive || current.ActiveInHierarchyUnchecked)
            current.AddComponentsUnchecked(result);
        foreach (var child in current._transform.ChildrenUnchecked)
        {
            CollectComponentsInChildren(child.GameObjectUnchecked, includeInactive, result);
        }
    }

    internal T? GetComponentInChildrenUnchecked<T>(bool includeInactive) where T : Component
    {
        if ((includeInactive || ActiveInHierarchyUnchecked) && GetComponentUnchecked<T>() is { } own) return own;
        foreach (var child in _transform.ChildrenUnchecked)
        {
            if (child.GameObjectUnchecked.GetComponentInChildrenUnchecked<T>(includeInactive) is { } component)
                return component;
        }
        return null;
    }
}
