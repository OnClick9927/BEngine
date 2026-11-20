namespace BEngine.Player;
/// <summary>Optional content counters supplied by the active content module.</summary>
public static class PlayerContentDiagnostics
{
    public static Func<PlayerContentPerformanceSnapshot>? Capture { get; set; }
    public static PlayerContentPerformanceSnapshot Snapshot => Capture?.Invoke() ?? new(false, []);
}
public sealed record PlayerContentPerformanceSnapshot(bool Enabled, PlayerContentPerformanceCounter[] Counters);
public sealed record PlayerContentPerformanceCounter(string Name, long Calls, long Bytes, double TotalMilliseconds, double MaximumMilliseconds);
