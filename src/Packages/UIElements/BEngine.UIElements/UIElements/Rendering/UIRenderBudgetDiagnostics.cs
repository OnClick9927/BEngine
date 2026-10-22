using BEngine.Diagnostics;

namespace BEngine.UIElements;

/// <summary>Last renderer submission; rectangle coverage is an estimate, not a GPU fragment counter.</summary>
public static class UIRenderBudgetDiagnostics
{
    public static int culledCommands { get; internal set; }
    public static double translucentRectangleCoverage { get; internal set; }
    public static long textMeasurementCacheHits { get; internal set; }
    static UIRenderBudgetDiagnostics() => RuntimeDiagnosticRegistry.Register("budget.ui.render", "UI rendering",
        new("culled", "Last culled commands", RuntimeDiagnosticUnit.Number, () => culledCommands),
        new("coverage", "Translucent rectangle coverage (estimate)", RuntimeDiagnosticUnit.Number, () => translucentRectangleCoverage),
        new("textCache", "Text measurement cache hits", RuntimeDiagnosticUnit.Number, () => textMeasurementCacheHits),
        new("commandCache", "Render command cache hits", RuntimeDiagnosticUnit.Number, () => UIRenderListBuilder.cacheHits),
        new("commandsRebuilt", "Last rebuilt commands", RuntimeDiagnosticUnit.Number, () => UIRenderListBuilder.lastRebuiltCommandCount));
}
