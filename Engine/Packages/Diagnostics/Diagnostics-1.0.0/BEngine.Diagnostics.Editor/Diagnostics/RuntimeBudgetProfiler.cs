using BEngine.Diagnostics;

namespace BEngine.Editor;

internal static class RuntimeBudgetProfiler
{
    private static readonly Dictionary<string, WeakReference<RuntimeDiagnosticModule>> Registered = [];
    private static readonly HashSet<(string Module, string Counter)> Faulted = [];
    internal static void Capture()
    {
        foreach (var module in RuntimeDiagnosticRegistry.modules.ToArray())
        {
            if (!Registered.TryGetValue(module.Id, out var weak) || !weak.TryGetTarget(out var previous) || !ReferenceEquals(previous, module))
            {
                EditorProfilerModuleRegistry.Register(new(module.Id, module.DisplayName, module.Counters.Select(counter =>
                    new EditorProfilerCounterDescriptor(counter.Name, counter.DisplayName,
                        (EditorProfilerCounterUnit)counter.Unit, new Color((Fix64).25, (Fix64).7, (Fix64).9, 1)))));
                Registered[module.Id] = new(module);
                Faulted.RemoveWhere(key => key.Module == module.Id);
            }
            foreach (var counter in module.Counters)
            {
                var key = (module.Id, counter.Name);
                try { EditorProfiler.ReportCounter(module.Id, counter.Name, counter.Read()); Faulted.Remove(key); }
                catch (Exception error) { if (Faulted.Add(key)) Debug.LogException(error); }
            }
        }
        foreach (var id in Registered.Keys.Where(id => !RuntimeDiagnosticRegistry.modules.Any(module => module.Id == id)).ToArray())
        { EditorProfilerModuleRegistry.Unregister(id); Registered.Remove(id); Faulted.RemoveWhere(key => key.Module == id); }
    }
}
