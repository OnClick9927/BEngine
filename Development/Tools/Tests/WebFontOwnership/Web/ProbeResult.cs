using System.Text.Json.Serialization;

internal sealed class ProbeResult
{
    public bool Passed { get; init; }
    public int Count { get; init; }
    public List<string> Checks { get; init; } = [];
    public string? Error { get; init; }
    public int MetricsCalls { get; init; }
    public long NativeAllocated { get; init; }
    public long LegacyAllocated { get; init; }
    public double NativeMilliseconds { get; init; }
    public double LegacyMilliseconds { get; init; }
    public bool AllocationCounterSupported { get; init; }
    public long AllocationProbeBytes { get; init; }
}

[JsonSerializable(typeof(ProbeResult))]
[JsonSerializable(typeof(string))]
internal partial class ProbeJson : JsonSerializerContext;
