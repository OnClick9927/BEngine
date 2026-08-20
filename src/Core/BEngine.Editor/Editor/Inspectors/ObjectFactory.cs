namespace BEngine.Editor;

public static class ObjectFactory
{
    public static event Action<Component>? componentWasAdded;

    public static T CreateInstance<T>() where T : ScriptableObject, new() =>
        ScriptableObject.CreateInstance<T>();

    public static ScriptableObject CreateInstance(Type type) => ScriptableObject.CreateInstance(type);

    public static GameObject CreateGameObject(string name, params Type[] componentTypes)
    {
        var gameObject = new GameObject(name);
        foreach (var componentType in componentTypes ?? []) AddComponent(gameObject, componentType);
        return gameObject;
    }

    public static T AddComponent<T>(GameObject gameObject) where T : Component, new() =>
        (T)AddComponent(gameObject, typeof(T));

    public static Component AddComponent(GameObject gameObject, Type componentType)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        ArgumentNullException.ThrowIfNull(componentType);
        var existing = gameObject.components.ToHashSet(ReferenceEqualityComparer.Instance);
        var component = gameObject.AddComponent(componentType);
        if (component is MonoBehaviour behaviour)
        {
            Invoke(behaviour.Reset, behaviour, nameof(MonoBehaviour.Reset));
            Invoke(behaviour.OnValidate, behaviour, nameof(MonoBehaviour.OnValidate));
        }
        var added = gameObject.components.Where(item => !existing.Contains(item)).Cast<BObject>().ToArray();
        Undo.RegisterCreatedObjectsUndo(added, $"Add {componentType.Name}");
        EditorCallbackDispatcher.Invoke(componentWasAdded, component, nameof(componentWasAdded));
        EditorUtility.SetDirty(component);
        EditorApplication.RaiseHierarchyChanged();
        return component;
    }

    private static void Invoke(Action callback, MonoBehaviour behaviour, string callbackName)
        => EditorFeatureGuard.Invoke(behaviour, callbackName, callback);
}
