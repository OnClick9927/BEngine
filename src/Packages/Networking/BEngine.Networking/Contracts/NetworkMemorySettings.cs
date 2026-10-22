namespace BEngine.Networking;

/// <summary>Admission limits for requests and buffered HTTP responses on the engine thread.</summary>
public static class NetworkMemorySettings
{
    static NetworkMemorySettings() => Diagnostics.RuntimeDiagnosticRegistry.Register("budget.http", "HTTP admission",
        new("active", "Active HTTP requests", Diagnostics.RuntimeDiagnosticUnit.Number, () => activeRequests),
        new("buffered", "Buffered response bytes", Diagnostics.RuntimeDiagnosticUnit.Bytes, () => bufferedBytes),
        new("limit", "Effective buffer budget", Diagnostics.RuntimeDiagnosticUnit.Bytes, () => effectiveBufferedBudgetBytes));
    public static int maximumConcurrentRequests { get; set; } = 64;
    public static long maximumResponseBytes { get; set; } = 32L * 1024 * 1024;
    public static long maximumBufferedBytes { get; set; } = 128L * 1024 * 1024;
    public static int activeRequests { get; private set; }
    public static long bufferedBytes { get; private set; }
    // The aggregate response limit, per-response limit and concurrency limit
    // are the admission contract. A GC estimate of the entire application is
    // not a reliable per-subsystem limit on every runtime.
    public static long effectiveBufferedBudgetBytes => Math.Max(0, maximumBufferedBytes);
    internal static void Enter()
    {
        if (maximumConcurrentRequests < 1 || activeRequests >= maximumConcurrentRequests)
            throw new InvalidDataException("HTTP concurrent request budget exceeded.");
        activeRequests++;
    }
    internal static void Exit() => activeRequests--;
    internal static void Reserve(long count)
    {
        if (count < 0 || count > effectiveBufferedBudgetBytes - bufferedBytes)
            throw new InvalidDataException("HTTP buffered response budget exceeded. Dispose completed requests or use a streaming download handler.");
        bufferedBytes += count;
    }
    internal static void Release(long count) => bufferedBytes -= count;
}
