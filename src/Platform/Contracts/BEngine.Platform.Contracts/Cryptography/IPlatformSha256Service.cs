namespace BEngine.Platform;

/// <summary>Host SHA-256 mechanisms, independent of resource formats and expected content hashes.</summary>
public interface IPlatformSha256Service
{
    IPlatformIncrementalSha256 CreateIncremental();
    bool CanComputeAsync(long byteCount);
    /// <summary>Copies or consumes input before returning. Null means temporary admission pressure;
    /// an accepted computation must finish or fail, rather than silently switching backends.</summary>
    IPlatformSha256Operation? TryBegin(ReadOnlySpan<byte> input);
}

public interface IPlatformIncrementalSha256 : IDisposable
{
    void Append(ReadOnlySpan<byte> input);
    void FinalizeHash(Span<byte> destination);
    void Reset();
}

public enum PlatformHashOperationStatus { Running, Succeeded, Failed }

/// <summary>Operation-owned digest. Poll is nonblocking; results are hexadecimal SHA-256.
/// Dispose releases native ownership, including when the native computation cannot be aborted.</summary>
public interface IPlatformSha256Operation : IDisposable
{
    PlatformHashOperationStatus Status { get; }
    string? Result { get; }
    Exception? Exception { get; }
    void Poll();
}
