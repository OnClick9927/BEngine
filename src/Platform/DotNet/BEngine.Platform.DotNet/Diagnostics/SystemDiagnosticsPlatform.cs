using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using BEngine.Instrumentation;

namespace BEngine.Platform.DotNet.Diagnostics;

public sealed class SystemDiagnosticsPlatform : IRuntimeDiagnosticsPlatform
{
    public DiagnosticThreadInfo CurrentThread => new(Environment.CurrentManagedThreadId, Thread.CurrentThread.Name);
    private readonly Lock _gate = new();
    private TimeSpan _lastCpu;
    private long _lastTimestamp;

    public DiagnosticProcessSample CaptureProcess()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var now = Stopwatch.GetTimestamp();
        var cpu = process.TotalProcessorTime;
        double percent;
        lock (_gate)
        {
            var wallSeconds = _lastTimestamp == 0 ? 0 : Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
            percent = wallSeconds <= 0 ? 0 : Math.Clamp((cpu - _lastCpu).TotalSeconds /
                (wallSeconds * Environment.ProcessorCount) * 100d, 0, 100);
            _lastTimestamp = now;
            _lastCpu = cpu;
        }
        return new(process.ProcessName, percent, Math.Max(0, process.WorkingSet64), Math.Max(0, process.PrivateMemorySize64));
    }

    public IDisposable ObserveAssemblyUnloading(Assembly assembly, Action unloading) =>
        new UnloadRegistration(AssemblyLoadContext.GetLoadContext(assembly), unloading);

    private sealed class UnloadRegistration : IDisposable
    {
        private AssemblyLoadContext? _context;
        private Action? _unloading;
        internal UnloadRegistration(AssemblyLoadContext? context, Action unloading)
        {
            _unloading = unloading;
            if (context?.IsCollectible == true) { _context = context; context.Unloading += OnUnloading; }
        }
        private void OnUnloading(AssemblyLoadContext _) => _unloading?.Invoke();
        public void Dispose()
        {
            var context = Interlocked.Exchange(ref _context, null);
            if (context is not null) context.Unloading -= OnUnloading;
            _unloading = null;
        }
    }
}
