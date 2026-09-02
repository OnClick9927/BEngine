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
            return GameObjectUnchecked;
        }
    }
    public Transform transform
    {
        get
        {
            return GameObjectUnchecked.TransformUnchecked;
        }
    }
    public bool enabled
    {
        get { return _enabled; }
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            SceneRuntime.NotifyComponentStateChanged(this);
        }
    }

    internal GameObject GameObjectUnchecked => _gameObject ??
        throw new InvalidOperationException("Component is not attached to a GameObject.");
    internal GameObject? GameObjectOrNull => _gameObject;
    internal bool EnabledUnchecked => _enabled;

    public T? GetComponent<T>() where T : Component
    {
        return GameObjectUnchecked.GetComponentUnchecked<T>();
    }

    public Component? GetComponent(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return GameObjectUnchecked.GetComponentUnchecked(type);
    }

    public T[] GetComponents<T>() where T : Component
    {
        return GameObjectUnchecked.GetComponentsUnchecked<T>();
    }

    public T? GetComponentInParent<T>() where T : Component
    {
        for (var current = GameObjectUnchecked.TransformUnchecked; current is not null;
             current = current.ParentUnchecked)
        {
            if (current.GameObjectUnchecked.GetComponentUnchecked<T>() is { } component) return component;
        }
        return null;
    }
    public T[] GetComponentsInParent<T>(bool includeInactive = false) where T : Component
    {
        return GameObjectUnchecked.GetComponentsInParentUnchecked<T>(includeInactive).ToArray();
    }

    public T? GetComponentInChildren<T>(bool includeInactive = false) where T : Component
    {
        return GameObjectUnchecked.GetComponentInChildrenUnchecked<T>(includeInactive);
    }

    public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
    {
        return GameObjectUnchecked.GetComponentsInChildrenUnchecked<T>(includeInactive).ToArray();
    }

    public bool TryGetComponent<T>(out T? component) where T : Component
    {
        component = GameObjectUnchecked.GetComponentUnchecked<T>();
        return component is not null;
    }

    public bool CompareTag(string value)
    {
        return GameObjectUnchecked.CompareTagUnchecked(value);
    }

    public virtual void OnReset() { }
    public virtual void OnDrawGizmos() { }
    public virtual void OnDrawGizmosSelected() { }

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
