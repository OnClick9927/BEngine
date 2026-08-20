using System.Collections;

namespace BEngine;

public abstract class Component : BObject
{
    private GameObject? _gameObject;
    private bool _enabled = true;

    public GameObject gameObject
    {
        get
        {
            MainThreadGuard.Ensure();
            return GameObjectUnchecked;
        }
    }
    public Transform transform
    {
        get
        {
            MainThreadGuard.Ensure();
            return GameObjectUnchecked.TransformUnchecked;
        }
    }
    public bool enabled
    {
        get { MainThreadGuard.Ensure(); return _enabled; }
        set
        {
            MainThreadGuard.Ensure();
            if (_enabled == value) return;
            _enabled = value;
            SceneRuntime.NotifyComponentStateChanged(this);
        }
    }

    internal GameObject GameObjectUnchecked => _gameObject ??
        throw new InvalidOperationException("Component is not attached to a GameObject.");
    internal bool EnabledUnchecked => _enabled;

    public T? GetComponent<T>() where T : Component
    {
        MainThreadGuard.Ensure();
        return GameObjectUnchecked.GetComponentUnchecked<T>();
    }

    public Component? GetComponent(Type type)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(type);
        return GameObjectUnchecked.GetComponentUnchecked(type);
    }

    public T[] GetComponents<T>() where T : Component
    {
        MainThreadGuard.Ensure();
        return GameObjectUnchecked.GetComponentsUnchecked<T>().ToArray();
    }

    public T? GetComponentInParent<T>() where T : Component
    {
        MainThreadGuard.Ensure();
        for (var current = GameObjectUnchecked.TransformUnchecked; current is not null;
             current = current.ParentUnchecked)
        {
            if (current.GameObjectUnchecked.GetComponentUnchecked<T>() is { } component) return component;
        }
        return null;
    }
    public T[] GetComponentsInParent<T>(bool includeInactive = false) where T : Component
    {
        MainThreadGuard.Ensure();
        return GameObjectUnchecked.GetComponentsInParentUnchecked<T>(includeInactive).ToArray();
    }

    public T? GetComponentInChildren<T>(bool includeInactive = false) where T : Component
    {
        MainThreadGuard.Ensure();
        return GameObjectUnchecked.GetComponentInChildrenUnchecked<T>(includeInactive);
    }

    public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
    {
        MainThreadGuard.Ensure();
        return GameObjectUnchecked.GetComponentsInChildrenUnchecked<T>(includeInactive).ToArray();
    }

    public bool TryGetComponent<T>(out T? component) where T : Component
    {
        MainThreadGuard.Ensure();
        component = GameObjectUnchecked.GetComponentUnchecked<T>();
        return component is not null;
    }

    public bool CompareTag(string value)
    {
        MainThreadGuard.Ensure();
        return GameObjectUnchecked.CompareTagUnchecked(value);
    }

    internal void Attach(GameObject owner)
    {
        if (_gameObject is not null)
        {
            throw new InvalidOperationException("A Component can only be attached once.");
        }

        _gameObject = owner;
        name = GetType().Name;
    }
}
