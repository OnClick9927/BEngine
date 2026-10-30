using System.Diagnostics;
using System.Threading;

namespace BEngine.HotUpdate;

/// <summary>Opt-in bounded CPU preparation counters; nested phase durations overlap.</summary>
public static class ManagedCodeStartupDiagnostics
{
    private static CounterSet? _current;
    private static int _enabled;
    public static bool Enabled
    {
        get => Volatile.Read(ref _enabled) != 0;
        set
        {
            if (value && Volatile.Read(ref _current) is null)
                Interlocked.CompareExchange(ref _current, new CounterSet(), null);
            Volatile.Write(ref _enabled, value ? 1 : 0);
        }
    }
    public static void Reset()
    {
        if (Volatile.Read(ref _current) is not null) Interlocked.Exchange(ref _current, new CounterSet());
    }
    public static ManagedCodeStartupCounter[] Snapshot
    {
        get
        {
            var set = Volatile.Read(ref _current);
            if (set is null) return [];
            var result = new ManagedCodeStartupCounter[set.Counters.Length];
            for (var i = 0; i < result.Length; i++)
            {
                var counter = set.Counters[i];
                result[i] = new(Name((Phase)i), Interlocked.Read(ref counter.Calls), Interlocked.Read(ref counter.Bytes),
                    Milliseconds(Interlocked.Read(ref counter.Ticks)), Milliseconds(Interlocked.Read(ref counter.MaximumTicks)));
            }
            return result;
        }
    }
    internal static Measurement Measure(Phase phase, long bytes = 0)
    {
        if (Volatile.Read(ref _enabled) == 0) return default;
        var set = Volatile.Read(ref _current);
        return set is null ? default : new(new Pending(set.Counters[(int)phase], bytes));
    }
    private static double Milliseconds(long ticks) => (double)ticks * 1000 / Stopwatch.Frequency;
    internal enum Phase
    {
        ReleaseValidate, PrecompiledValidateAndClaim, PrecompiledRegistrationSnapshot,
        PrecompiledImageCopy, PrecompiledSha256, PrecompiledMetadataAndSymbols,
        PrecompiledValidateLoadedAssemblies, PrecompiledClaim, RuntimeModuleOrder,
        RuntimeLoadModule, RuntimeModuleInitializers, RuntimeModuleDiscovery,
        RuntimeGetTypes, RuntimeModuleConstructors, GateRuntimeLoadSynchronousPrefix,
        GateValidateAndOrderModules, GateUnregisterAssemblies, GateRegisterAssemblies,
        GatePreparedDomain, Count
    }
    private static string Name(Phase phase) => phase switch
    {
        Phase.ReleaseValidate => "runtime.releaseValidate",
        Phase.PrecompiledValidateAndClaim => "precompiled.validateAndClaim",
        Phase.PrecompiledRegistrationSnapshot => "precompiled.registrationSnapshot",
        Phase.PrecompiledImageCopy => "precompiled.imageCopy",
        Phase.PrecompiledSha256 => "precompiled.sha256",
        Phase.PrecompiledMetadataAndSymbols => "precompiled.metadataAndSymbols",
        Phase.PrecompiledValidateLoadedAssemblies => "precompiled.validateLoadedAssemblies",
        Phase.PrecompiledClaim => "precompiled.claim",
        Phase.RuntimeModuleOrder => "runtime.moduleOrder",
        Phase.RuntimeLoadModule => "runtime.loadModule",
        Phase.RuntimeModuleInitializers => "runtime.moduleInitializers",
        Phase.RuntimeModuleDiscovery => "runtime.moduleDiscovery",
        Phase.RuntimeGetTypes => "runtime.getTypes",
        Phase.RuntimeModuleConstructors => "runtime.moduleConstructors",
        Phase.GateRuntimeLoadSynchronousPrefix => "gate.runtimeLoadSynchronousPrefix",
        Phase.GateValidateAndOrderModules => "gate.validateAndOrderModules",
        Phase.GateUnregisterAssemblies => "gate.unregisterAssemblies",
        Phase.GateRegisterAssemblies => "gate.registerAssemblies",
        Phase.GatePreparedDomain => "gate.preparedDomain",
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };
    internal readonly struct Measurement(Pending? pending) : IDisposable
    {
        public void Dispose() => pending?.Complete();
    }
    internal sealed class Pending(Counter counter, long bytes)
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        private int _completed;
        internal void Complete()
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0) return;
            var elapsed = Stopwatch.GetTimestamp() - _started;
            Interlocked.Increment(ref counter.Calls); Interlocked.Add(ref counter.Bytes, bytes); Interlocked.Add(ref counter.Ticks, elapsed);
            var maximum = Interlocked.Read(ref counter.MaximumTicks);
            while (elapsed > maximum)
            {
                var old = Interlocked.CompareExchange(ref counter.MaximumTicks, elapsed, maximum);
                if (old == maximum) break;
                maximum = old;
            }
        }
    }
    internal sealed class Counter { internal long Calls, Bytes, Ticks, MaximumTicks; }
    private sealed class CounterSet
    {
        internal readonly Counter[] Counters = Enumerable.Range(0, (int)Phase.Count).Select(static _ => new Counter()).ToArray();
    }
}
public readonly record struct ManagedCodeStartupCounter(string Name, long Calls, long Bytes,
    double TotalMilliseconds, double MaximumMilliseconds);
