namespace BEngine.Diagnostics;
/// <summary>Reads a frozen CLR process snapshot or dump; the reader owns platform-specific tooling.</summary>
public interface IManagedHeapReader
{
    ManagedHeapSnapshot CaptureProcess(int processId, int maximumObjects = 1_000_000, CancellationToken cancellationToken = default);
    ManagedHeapSnapshot ReadDump(string path, int maximumObjects = 1_000_000, CancellationToken cancellationToken = default);
}
