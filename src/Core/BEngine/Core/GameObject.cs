using BEngine.Entities;

namespace BEngine;

public class GameObject : BObject
{
    private readonly List<Component> _components = [];
    private readonly ThreadGuardedReadOnlyList<Component> _componentsView;
    private bool _activeSelf = true;
    private bool _dontDestroyOnLoad;
    private string _tag = "Untagged";
    private ulong _layer = SortingLayer.Default;
    private bool _isStatic;
    private Transform _transform;
    private Scene? _scene;
    private Entity _entity;

    public bool activeSelf
    {
        get { MainThreadGuard.Ensure(); return _activeSelf; }
        set
        {
            MainThreadGuard.Ensure();
            if (_activeSelf == value) return;
            _activeSelf = value;
            SynchronizeActiveStateHierarchy();
            SceneRuntime.NotifyHierarchyStateChanged(this);
        }
    }
    public bool activeInHierarchy
    {
        get { MainThreadGuard.Ensure(); return ActiveInHierarchyUnchecked; }
    }
    public string tag
    {
        get { MainThreadGuard.Ensure(); return _tag; }
        set
        {
            MainThreadGuard.Ensure();
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            var normalized = value.Trim();
            if (!TagManager.IsDefined(normalized))
                throw new ArgumentException($"Tag '{normalized}' is not defined.", nameof(value));
            _tag = normalized;
        }
    }
    public ulong layer
    {
        get { MainThreadGuard.Ensure(); return _layer; }
        set
        {
            MainThreadGuard.Ensure();
            SortingLayer.Validate(value);
            _layer = value;
        }
    }
    public bool isStatic
    {
        get { MainThreadGuard.Ensure(); return _isStatic; }
        set
        {
            MainThreadGuard.Ensure();
            _isStatic = value;
        }
    }
    public bool isDontDestroyOnLoad
    {
        get { MainThreadGuard.Ensure(); return _transform.RootUnchecked.GameObjectUnchecked._dontDestroyOnLoad; }
    }
    public Transform transform
    {
        get { MainThreadGuard.Ensure(); return _transform; }
        private set => _transform = value;
    }
    public Scene? scene
    {
        get { MainThreadGuard.Ensure(); return _scene; }
        internal set => _scene = value;
    }
    public Entity entity
    {
        get { MainThreadGuard.Ensure(); return _entity; }
        private set => _entity = value;
    }
    public IReadOnlyList<Component> components
    {
        get { MainThreadGuard.Ensure(); return _componentsView; }
    }

    internal bool ActiveInHierarchyUnchecked => _activeSelf &&
        (_transform.ParentUnchecked is null || _transform.ParentUnchecked.GameObjectUnchecked.ActiveInHierarchyUnchecked);
    internal Transform TransformUnchecked => _transform;
    internal Scene? SceneUnchecked => _scene;
    internal Entity EntityUnchecked => _entity;
    internal IReadOnlyList<Component> ComponentsUnchecked => _components;

    public GameObject(string name = "GameObject")
    {
        _componentsView = new ThreadGuardedReadOnlyList<Component>(_components);
        this.name = name;
        _transform = new Transform();
        Attach(_transform);
    }

    public T AddComponent<T>() where T : Component, new() => (T)AddComponent(typeof(T));

    public Component AddComponent(Type componentType)
    {
        MainThreadGuard.Ensure();
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
            replacement.Attach(this);
            _transform.TransferTo(replacement);
            var transformIndex = _components.IndexOf(_transform);
            _scene?.WorldUnchecked.EntityManager.ReplaceManagedComponent(_entity, _transform, replacement);
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
        component.Attach(this);
        _components.Add(component);
        _scene?.WorldUnchecked.EntityManager.AddManagedComponent(_entity, component);
        SceneRuntime.NotifyComponentStateChanged(component);
        return component;
    }

    public T? GetComponent<T>() where T : Component
    {
        MainThreadGuard.Ensure();
        return GetComponentUnchecked<T>();
    }

    public Component? GetComponent(Type type)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(type);
        return GetComponentUnchecked(type);
    }

    public IReadOnlyList<T> GetComponents<T>() where T : Component
    {
        MainThreadGuard.Ensure();
        return GetComponentsUnchecked<T>();
    }

    internal T? GetComponentUnchecked<T>() where T : Component => _scene is null
        ? _components.OfType<T>().FirstOrDefault()
        : _scene.WorldUnchecked.EntityManager.GetManagedComponent<T>(_entity);

    internal Component? GetComponentUnchecked(Type type) => _scene is null
        ? _components.FirstOrDefault(type.IsInstanceOfType)
        : _scene.WorldUnchecked.EntityManager.GetManagedComponents<Component>(_entity)
            .FirstOrDefault(type.IsInstanceOfType);

    internal IReadOnlyList<T> GetComponentsUnchecked<T>() where T : Component => _scene is null
        ? _components.OfType<T>().ToArray()
        : _scene.WorldUnchecked.EntityManager.GetManagedComponents<T>(_entity);

    public bool TryGetComponent<T>(out T? component) where T : Component
    {
        MainThreadGuard.Ensure();
        component = GetComponentUnchecked<T>();
        return component is not null;
    }

    public T? GetComponentInParent<T>() where T : Component
    {
        MainThreadGuard.Ensure();
        for (var current = _transform; current is not null; current = current.ParentUnchecked)
        {
            if (current.GameObjectUnchecked.GetComponentUnchecked<T>() is { } component) return component;
        }
        return null;
    }

    public T? GetComponentInChildren<T>(bool includeInactive = false) where T : Component
    {
        MainThreadGuard.Ensure();
        return GetComponentInChildrenUnchecked<T>(includeInactive);
    }

    public IReadOnlyList<T> GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
    {
        MainThreadGuard.Ensure();
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
        MainThreadGuard.Ensure();
        return GetComponentsInParentUnchecked<T>(includeInactive);
    }

    internal IReadOnlyList<T> GetComponentsInParentUnchecked<T>(bool includeInactive) where T : Component
    {
        var result = new List<T>();
        for (var current = _transform; current is not null; current = current.ParentUnchecked)
        {
            var owner = current.GameObjectUnchecked;
            if (includeInactive || owner.ActiveInHierarchyUnchecked)
                result.AddRange(owner.GetComponentsUnchecked<T>());
        }
        return result;
    }

    public static GameObject? Find(string name)
    {
        MainThreadGuard.Ensure();
        return FindObjectsByType<GameObject>()
            .FirstOrDefault(item => item.ActiveInHierarchyUnchecked && item.name == name);
    }

    public static GameObject? FindWithTag(string tag)
    {
        MainThreadGuard.Ensure();
        return FindGameObjectsWithTag(tag).FirstOrDefault();
    }

    public static GameObject[] FindGameObjectsWithTag(string tag)
    {
        MainThreadGuard.Ensure();
        if (!TagManager.IsDefined(tag))
            throw new ArgumentException($"Tag '{tag}' is not defined.", nameof(tag));
        var normalized = tag.Trim();
        return FindObjectsByType<GameObject>()
            .Where(item => item.ActiveInHierarchyUnchecked && item.CompareTagUnchecked(normalized)).ToArray();
    }

    public void SetActive(bool value) => activeSelf = value;
    public bool CompareTag(string value)
    {
        MainThreadGuard.Ensure();
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
        MainThreadGuard.Ensure();
        if (!CanRemoveComponent(component)) return false;
        SceneRuntime.NotifyComponentDestroying(component);
        _scene?.WorldUnchecked.EntityManager.RemoveManagedComponent(_entity, component);
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
        _scene?.WorldUnchecked.EntityManager.AddManagedComponent(_entity, component);
        SceneRuntime.NotifyComponentStateChanged(component);
        return true;
    }

    internal void BindToScene(Scene owner, Entity sceneEntity)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_scene is not null || !_entity.IsNull)
            throw new InvalidOperationException("GameObject is already bound to an ECS World.");
        _scene = owner;
        _entity = sceneEntity;
        owner.WorldUnchecked.EntityManager.AddComponentData(_entity, new EntityGuid(Id));
        owner.WorldUnchecked.EntityManager.AddComponentData(_entity,
            new EntityActiveState(_activeSelf, ActiveInHierarchyUnchecked));
        owner.WorldUnchecked.EntityManager.AddComponentData(_entity, new LocalTransform(
            _transform.LocalPositionUnchecked, _transform.LocalRotationUnchecked,
            _transform.LocalScaleUnchecked));
        owner.WorldUnchecked.EntityManager.AddManagedComponent(_entity, this);
        foreach (var component in _components)
            owner.WorldUnchecked.EntityManager.AddManagedComponent(_entity, component);
    }

    internal void UnbindFromScene()
    {
        _transform.CaptureEntityState();
        _scene = null;
        _entity = Entity.Null;
    }

    internal void RestoreSceneBinding(Scene owner, Entity sceneEntity)
    {
        _scene = owner;
        _entity = sceneEntity;
    }

    internal void MarkDontDestroyOnLoad() => _transform.RootUnchecked.GameObjectUnchecked._dontDestroyOnLoad = true;

    internal void ClearDontDestroyOnLoad() => _transform.RootUnchecked.GameObjectUnchecked._dontDestroyOnLoad = false;

    internal void SynchronizeActiveStateHierarchy()
    {
        if (_scene is not null && _scene.WorldUnchecked.EntityManager.Exists(_entity))
        {
            ref var state = ref _scene.WorldUnchecked.EntityManager.GetComponentDataRW<EntityActiveState>(_entity);
            state.ActiveSelf = _activeSelf;
            state.ActiveInHierarchy = ActiveInHierarchyUnchecked;
        }
        foreach (var child in _transform.ChildrenUnchecked)
            child.GameObjectUnchecked.SynchronizeActiveStateHierarchy();
    }

    private static void CollectComponentsInChildren<T>(GameObject current, bool includeInactive, List<T> result)
        where T : Component
    {
        if (includeInactive || current.ActiveInHierarchyUnchecked)
            result.AddRange(current.GetComponentsUnchecked<T>());
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
