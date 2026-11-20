using System.Reflection;

namespace BEngine.Instrumentation;

public readonly record struct DiagnosticProcessSample(string Name, double CpuPercent, long WorkingSetBytes, long PrivateBytes);
public readonly record struct DiagnosticThreadInfo(int Id, string? Name);

/// <summary>System inspection and unload notifications supplied by the platform host.</summary>
public interface IRuntimeDiagnosticsPlatform
{
    DiagnosticThreadInfo CurrentThread { get; }
    DiagnosticProcessSample CaptureProcess();
    IDisposable ObserveAssemblyUnloading(Assembly assembly, Action unloading);
}

public static class DiagnosticsPlatform
{
    private static Func<IRuntimeDiagnosticsPlatform?>? _resolveService;

    internal static void SetServiceResolver(Func<IRuntimeDiagnosticsPlatform?> resolveService) =>
        _resolveService = resolveService ?? throw new ArgumentNullException(nameof(resolveService));

    public static IDisposable ObserveAssemblyUnloading(Assembly assembly, Action unloading)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(unloading);
        return _resolveService?.Invoke()?
            .ObserveAssemblyUnloading(assembly, unloading) ?? EmptyRegistration.Instance;
    }

    private sealed class EmptyRegistration : IDisposable
    {
        internal static readonly EmptyRegistration Instance = new();
        public void Dispose() { }
    }
}

