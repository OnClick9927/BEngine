using System.Runtime.Loader;

namespace BEngine.Diagnostics;

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
        if (counters.Length is < 1 or > 64 || counters.Select(counter => counter.Name).Distinct().Count() != counters.Length)
            throw new ArgumentException("Diagnostic modules require 1–64 uniquely named counters.");
        var module = new RuntimeDiagnosticModule(id, displayName, Array.AsReadOnly(counters));
        Modules[id] = module;
        return new Registration(module, AssemblyLoadContext.GetLoadContext(counters[0].Read.Method.Module.Assembly));
    }
    private sealed class Registration : IDisposable
    {
        private RuntimeDiagnosticModule? _module;
        private readonly AssemblyLoadContext? _context;
        internal Registration(RuntimeDiagnosticModule module, AssemblyLoadContext? context)
        { _module = module; _context = context; if (context?.IsCollectible == true) context.Unloading += Unloading; }
        private void Unloading(AssemblyLoadContext _) => Dispose();
        public void Dispose()
        {
            if (_module is not { } module) return; _module = null;
            if (Modules.TryGetValue(module.Id, out var current) && ReferenceEquals(current, module)) Modules.Remove(module.Id);
            if (_context?.IsCollectible == true) _context.Unloading -= Unloading;
        }
    }
}
