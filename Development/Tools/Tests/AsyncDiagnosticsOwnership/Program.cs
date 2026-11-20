using System.Runtime.CompilerServices;
using BEngine;
using BEngine.Instrumentation;

var asyncAssembly = typeof(CooperativeWorkScheduler).Assembly;
Check(asyncAssembly.GetName().Name == "BEngine.Core" && asyncAssembly == typeof(RuntimeDiagnosticRegistry).Assembly,
    "Async scheduling and instrumentation belong to Core");

RuntimeHelpers.RunModuleConstructor(typeof(LoadingWorkDiagnostics).Module.ModuleHandle);
var modules = RuntimeDiagnosticRegistry.modules.ToDictionary(module => module.Id);
Check(modules.TryGetValue("budget.async", out var cooperative) && cooperative.Counters.Count == 7,
    "Cooperative scheduler counters were not registered");
Check(modules.TryGetValue("budget.workers", out var workers) && workers.Counters.Count == 5,
    "Background scheduler counters were not registered");
Check(cooperative!.Counters.All(counter => double.IsFinite(counter.Read())) &&
      workers!.Counters.All(counter => double.IsFinite(counter.Read())),
    "Scheduler counters cannot be read");

RuntimeAssemblyCleanup.UnregisterExtensions(typeof(LoadingWorkDiagnostics).Assembly);
Check(!RuntimeDiagnosticRegistry.modules.Any(module => module.Id is "budget.async" or "budget.workers"),
    "Scheduler counters survived Diagnostics unload");
Console.WriteLine("ASYNC_DIAGNOSTICS_OWNERSHIP_OK");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
