using System.Runtime.CompilerServices;
using BEngine.Instrumentation;

namespace BEngine.Diagnostics;

internal static class AsyncSchedulerCounters
{
    [ModuleInitializer]
    internal static void Register()
    {
        var cooperative = RuntimeDiagnosticRegistry.Register("budget.async", "Cooperative work",
            new("pending", "Pending work", RuntimeDiagnosticUnit.Number, () => CooperativeWorkScheduler.Main.PendingCount),
            new("budget", "Shared frame budget", RuntimeDiagnosticUnit.Milliseconds, () => CooperativeWorkScheduler.Main.BudgetMilliseconds),
            new("period", "Target frame period", RuntimeDiagnosticUnit.Milliseconds, () => FrameWorkBudget.framePeriodMilliseconds),
            new("effective", "Effective frame budget", RuntimeDiagnosticUnit.Milliseconds, () => FrameWorkBudget.budgetMilliseconds),
            new("elapsed", "Last frame work", RuntimeDiagnosticUnit.Milliseconds, () => CooperativeWorkScheduler.Main.Snapshot.Milliseconds),
            new("longest", "Longest step", RuntimeDiagnosticUnit.Milliseconds, () => CooperativeWorkScheduler.Main.Snapshot.LongestStepMilliseconds),
            new("overruns", "Budget overruns", RuntimeDiagnosticUnit.Number, () => CooperativeWorkScheduler.Main.Snapshot.OverBudgetFrames));
        var background = RuntimeDiagnosticRegistry.Register("budget.workers", "Background workers",
            new("pending", "Pending data work", RuntimeDiagnosticUnit.Number, () => BackgroundWorkScheduler.Main.Snapshot.Pending),
            new("active", "Active data work", RuntimeDiagnosticUnit.Number, () => BackgroundWorkScheduler.Main.Snapshot.Active),
            new("workers", "Worker limit", RuntimeDiagnosticUnit.Number, () => BackgroundWorkScheduler.Main.Snapshot.WorkerLimit),
            new("completed", "Completed data work", RuntimeDiagnosticUnit.Number, () => BackgroundWorkScheduler.Main.Snapshot.Completed),
            new("rejected", "Rejected data work", RuntimeDiagnosticUnit.Number, () => BackgroundWorkScheduler.Main.Snapshot.Rejected));
        RuntimeAssemblyCleanup.Register(assembly =>
        {
            if (assembly != typeof(AsyncSchedulerCounters).Assembly) return;
            cooperative.Dispose();
            background.Dispose();
        });
    }
}
