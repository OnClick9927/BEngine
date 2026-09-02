namespace BEngine.Editor;

/// <summary>Global registry used to extend the Profiler module list from project and package code.</summary>
public static class EditorProfilerModuleRegistry
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, EditorProfilerModuleDefinition> Modules =
        new(StringComparer.Ordinal);

    public static event Action? modulesChanged;

    public static IDisposable Register(EditorProfilerModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        lock (Gate) Modules[module.Id] = module;
        EditorCallbackDispatcher.Invoke(modulesChanged, nameof(modulesChanged));
        return new Registration(module.Id, module);
    }

    public static bool Unregister(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        bool removed;
        lock (Gate) removed = Modules.Remove(id);
        if (removed) EditorCallbackDispatcher.Invoke(modulesChanged, nameof(modulesChanged));
        return removed;
    }

    public static EditorProfilerModuleDefinition[] GetModules()
    {
        lock (Gate) return Modules.Values.OrderBy(module => module.DisplayName,
            StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private sealed class Registration(
        string id,
        EditorProfilerModuleDefinition registeredModule) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            var removed = false;
            lock (Gate)
            {
                if (Modules.TryGetValue(id, out var current) &&
                    ReferenceEquals(current, registeredModule))
                    removed = Modules.Remove(id);
            }
            if (removed) EditorCallbackDispatcher.Invoke(modulesChanged, nameof(modulesChanged));
        }
    }
}
