namespace BEngine;

public interface ISceneRuntimeSystem
{
    string packageId => string.Empty;
    int order => 0;
    void Start(Scene scene) { }
    void FixedUpdate(Scene scene, Fix64 fixedDeltaTime) { }
    void Update(Scene scene, Fix64 deltaTime) { }
    void Stop(Scene scene) { }
}

public static class RuntimePackageState
{
    private static readonly Dictionary<string, bool> States = new(StringComparer.OrdinalIgnoreCase);
    public static bool IsEnabled(string packageId) => string.IsNullOrWhiteSpace(packageId) ||
        !States.TryGetValue(packageId, out var enabled) || enabled;
    public static void SetEnabled(string packageId, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        States[packageId] = enabled;
    }
}

public static class RuntimeSystemRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Func<ISceneRuntimeSystem>> Factories =
        new(StringComparer.Ordinal);

    public static void Register(string id, Func<ISceneRuntimeSystem> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        lock (Sync) Factories[id] = factory;
    }

    public static bool Unregister(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (Sync) return Factories.Remove(id);
    }

    internal static ISceneRuntimeSystem[] CreateSystems()
    {
        lock (Sync)
        {
            var registered = Factories
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value());
            var discovered = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(GetLoadableTypes)
                .Where(type => type.IsClass && !type.IsAbstract &&
                               typeof(ISceneRuntimeSystem).IsAssignableFrom(type) &&
                               type.GetConstructor(System.Reflection.BindingFlags.Instance |
                                   System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                                   binder: null, Type.EmptyTypes, modifiers: null) is not null)
                .Where(type => !Factories.Values.Any(factory => factory.Method.ReturnType == type))
                .Select(type => (ISceneRuntimeSystem)Activator.CreateInstance(type, nonPublic: true)!);
            return registered.Concat(discovered)
                .Where(system => RuntimePackageState.IsEnabled(system.packageId))
                .OrderBy(system => system.order)
                .ThenBy(system => system.GetType().FullName, StringComparer.Ordinal)
                .ToArray();
        }
    }

    private static Type[] GetLoadableTypes(System.Reflection.Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (System.Reflection.ReflectionTypeLoadException exception)
        { return exception.Types.Where(type => type is not null).Cast<Type>().ToArray(); }
    }
}
