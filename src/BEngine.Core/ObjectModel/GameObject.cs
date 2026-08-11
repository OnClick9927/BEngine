namespace BEngine;

public class GameObject : BObject
{
    private readonly List<Component> _components = [];

    public bool activeSelf { get; set; } = true;
    public bool activeInHierarchy => activeSelf &&
        (transform.parent is null || transform.parent.gameObject.activeInHierarchy);
    public string tag { get; set; } = "Untagged";
    public int layer { get; set; }
    public bool isStatic { get; set; }
    public Transform transform { get; private set; }
    public Scene? scene { get; internal set; }
    public IReadOnlyList<Component> components => _components;

    public GameObject(string name = "GameObject")
    {
        this.name = name;
        transform = new Transform();
        Attach(transform);
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
            if (componentType == typeof(Transform) || transform.GetType() != typeof(Transform))
            {
                throw new InvalidOperationException("Every GameObject owns exactly one Transform.");
            }

            var replacement = (Transform?)Activator.CreateInstance(componentType, nonPublic: true) ??
                throw new InvalidOperationException($"Unable to create {componentType.FullName}.");
            replacement.Id = transform.Id;
            replacement.Attach(this);
            transform.TransferTo(replacement);
            var transformIndex = _components.IndexOf(transform);
            _components[transformIndex] = replacement;
            transform = replacement;
            return replacement;
        }

        if (componentType.GetCustomAttributes(typeof(DisallowMultipleComponentAttribute), true).Length > 0 &&
            _components.Any(componentType.IsInstanceOfType))
        {
            throw new InvalidOperationException($"{componentType.Name} cannot be added more than once.");
        }

        foreach (var requirement in componentType.GetCustomAttributes(typeof(RequireComponentAttribute), true)
                     .Cast<RequireComponentAttribute>())
        {
            foreach (var requiredType in requirement.requiredComponents.Where(type => type is not null))
            {
                if (GetComponent(requiredType) is null) AddComponent(requiredType);
            }
        }

        var component = (Component?)Activator.CreateInstance(componentType, nonPublic: true) ??
            throw new InvalidOperationException($"Unable to create {componentType.FullName}.");
        Attach(component);
        return component;
    }

    internal Component Attach(Component component)
    {
        component.Attach(this);
        _components.Add(component);
        return component;
    }

    public T? GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();
    public Component? GetComponent(Type type) => _components.FirstOrDefault(type.IsInstanceOfType);
    public IReadOnlyList<T> GetComponents<T>() where T : Component => _components.OfType<T>().ToArray();

    public bool TryGetComponent<T>(out T? component) where T : Component
    {
        component = GetComponent<T>();
        return component is not null;
    }

    public T? GetComponentInParent<T>() where T : Component
    {
        for (var current = transform; current is not null; current = current.parent)
        {
            if (current.gameObject.GetComponent<T>() is { } component) return component;
        }
        return null;
    }

    public T? GetComponentInChildren<T>(bool includeInactive = false) where T : Component
    {
        if ((includeInactive || activeInHierarchy) && GetComponent<T>() is { } own) return own;
        foreach (var child in transform.children)
        {
            if (child.gameObject.GetComponentInChildren<T>(includeInactive) is { } component) return component;
        }
        return null;
    }

    public IReadOnlyList<T> GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
    {
        var result = new List<T>();
        CollectComponentsInChildren(this, includeInactive, result);
        return result;
    }

    public void SetActive(bool value) => activeSelf = value;
    public bool CompareTag(string value) => string.Equals(tag, value, StringComparison.Ordinal);

    public bool RemoveComponent(Component component)
    {
        if (ReferenceEquals(component, transform))
        {
            return false;
        }

        return _components.Remove(component);
    }

    private static void CollectComponentsInChildren<T>(GameObject current, bool includeInactive, List<T> result)
        where T : Component
    {
        if (includeInactive || current.activeInHierarchy) result.AddRange(current.GetComponents<T>());
        foreach (var child in current.transform.children)
        {
            CollectComponentsInChildren(child.gameObject, includeInactive, result);
        }
    }
}
