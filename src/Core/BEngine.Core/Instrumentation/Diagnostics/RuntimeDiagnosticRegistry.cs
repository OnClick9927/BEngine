namespace BEngine.Instrumentation;

public enum RuntimeDiagnosticUnit { Number, Milliseconds, Bytes, Percentage }
public sealed record RuntimeDiagnosticCounter(string Name, string DisplayName, RuntimeDiagnosticUnit Unit, Func<double> Read);
public sealed record RuntimeDiagnosticModule(string Id, string DisplayName, IReadOnlyList<RuntimeDiagnosticCounter> Counters);

/// <summary>Optional counters contributed without a dependency on the editor or Diagnostics package.</summary>
public static class RuntimeDiagnosticRegistry
{
    private static readonly Dictionary<string, RuntimeDiagnosticModule> Modules = new(StringComparer.Ordinal);
    public static IReadOnlyCollection<RuntimeDiagnosticModule> modules => Modules.Values;
    public static IDisposable Register(string id, string displayName, params RuntimeDiagnosticCounter[] counters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id); ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (counters.Length is < 1 or > 64 || !HaveUniqueNames(counters))
            throw new ArgumentException("Diagnostic modules require 1–64 uniquely named counters.");
        var module = new RuntimeDiagnosticModule(id, displayName, Array.AsReadOnly(counters));
        Modules[id] = module;
        return new Registration(module);
    }
    private static bool HaveUniqueNames(RuntimeDiagnosticCounter[] counters)
    {
        if (counters.Length > 8)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var counter in counters)
                if (!names.Add(counter.Name)) return false;
            return true;
        }
        for (var index = 0; index < counters.Length; index++)
        {
            var name = counters[index].Name;
            for (var previous = 0; previous < index; previous++)
                if (string.Equals(name, counters[previous].Name, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private sealed class Registration : IDisposable
    {
        private RuntimeDiagnosticModule? _module;
        private readonly IDisposable _unloading;
        internal Registration(RuntimeDiagnosticModule module)
        {
            _module = module;
            _unloading = DiagnosticsPlatform.ObserveAssemblyUnloading(module.Counters[0].Read.Method.Module.Assembly, Dispose);
        }
        public void Dispose()
        {
            if (_module is not { } module) return; _module = null;
            if (Modules.TryGetValue(module.Id, out var current) && ReferenceEquals(current, module)) Modules.Remove(module.Id);
            _unloading.Dispose();
        }
    }
}
