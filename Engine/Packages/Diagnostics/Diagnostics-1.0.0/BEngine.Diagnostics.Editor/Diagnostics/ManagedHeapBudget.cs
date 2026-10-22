namespace BEngine.Editor.Diagnostics;

public static class ManagedHeapBudget
{
    public static long maximumEstimatedAnalysisBytes { get; set; } = 512L * 1024 * 1024;
    public static long effectiveEstimatedAnalysisBytes
    {
        get
        {
            var configured = Math.Max(0, maximumEstimatedAnalysisBytes);
            long heap;
            try { heap = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes; }
            catch (PlatformNotSupportedException) { heap = 0; }
            catch (InvalidOperationException) { heap = 0; }
            return heap <= 0 || heap == long.MaxValue ? configured : Math.Min(configured,
                Math.Max(0, heap - Math.Min(96L * 1024 * 1024, heap / 4) - GC.GetTotalMemory(false)));
        }
    }
    internal static void Check(int objects, int references, int roots)
    {
        var estimate = checked((long)objects * 192 + (long)references * 48 + (long)roots * 40);
        if (estimate > effectiveEstimatedAnalysisBytes)
            throw new InvalidOperationException($"Managed heap analysis estimate {estimate} bytes exceeds budget {effectiveEstimatedAnalysisBytes}. Capture fewer objects or increase the analysis budget.");
    }
}
