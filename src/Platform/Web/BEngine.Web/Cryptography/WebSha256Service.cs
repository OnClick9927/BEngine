using System.Runtime.InteropServices.JavaScript;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BEngine.Platform;

public sealed class WebSha256Service : IPlatformSha256Service
{
    public const int MaximumAsyncInputBytes = 64 * 1024 * 1024;
    public static WebSha256Service Shared { get; } = new();
    public IPlatformIncrementalSha256 CreateIncremental() => new ManagedStreamingSha256();
    public bool CanComputeAsync(long byteCount)
    {
        if (!OperatingSystem.IsBrowser() || byteCount < 0 || byteCount > MaximumAsyncInputBytes) return false;
        try { return WebSha256Interop.Available(); }
        catch (JSException) { return false; } // Older hosts retain the bounded managed mechanism.
    }
    public IPlatformSha256Operation? TryBegin(ReadOnlySpan<byte> input)
    {
        if (!OperatingSystem.IsBrowser() || !CanComputeAsync(input.Length)) throw new PlatformNotSupportedException("Native SHA-256 is unavailable for this payload.");
        // JS interop requires a writable memory view. The host synchronously
        // copies this borrowed view and never writes to it or retains it.
        var handle = WebSha256Interop.Begin(MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(input), input.Length));
        if (handle == 0) return null;
        if (handle < 0) throw new InvalidDataException("Native SHA-256 returned an invalid handle.");
        try { return new Operation(handle); }
        catch { WebSha256Interop.Release(handle); throw; }
    }

    [SupportedOSPlatform("browser")]
    private sealed class Operation(int handle) : IPlatformSha256Operation
    {
        private int _handle = handle;
        private bool _disposed;
        public PlatformHashOperationStatus Status { get; private set; }
        public string? Result { get; private set; }
        public Exception? Exception { get; private set; }
        public void Poll()
        {
            if (_disposed || Status != PlatformHashOperationStatus.Running) return;
            try
            {
                var status = WebSha256Interop.Status(_handle);
                if (status == 0) return;
                if (status == -1) throw new IOException(WebSha256Interop.Error(_handle) ?? "Native SHA-256 failed.");
                if (status != 1) throw new InvalidDataException("Native SHA-256 returned an invalid status.");
                Result = WebSha256Interop.Result(_handle);
                Status = PlatformHashOperationStatus.Succeeded;
            }
            catch (Exception error) { Exception = error; Status = PlatformHashOperationStatus.Failed; }
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            if (Status == PlatformHashOperationStatus.Running)
            { Status = PlatformHashOperationStatus.Failed; Exception = new IOException("Native SHA-256 operation was released."); }
            var handle = _handle; _handle = 0;
            if (handle > 0) try { WebSha256Interop.Release(handle); } catch { }
        }
    }
}

[SupportedOSPlatform("browser")]
internal static partial class WebSha256Interop
{
    [JSImport("globalThis.BEngineWebContentHash.available")] internal static partial bool Available();
    [JSImport("globalThis.BEngineWebContentHash.begin")] internal static partial int Begin([JSMarshalAs<JSType.MemoryView>] Span<byte> bytes);
    [JSImport("globalThis.BEngineWebContentHash.status")] internal static partial int Status(int handle);
    [JSImport("globalThis.BEngineWebContentHash.result")] internal static partial string? Result(int handle);
    [JSImport("globalThis.BEngineWebContentHash.error")] internal static partial string? Error(int handle);
    [JSImport("globalThis.BEngineWebContentHash.release")] internal static partial void Release(int handle);
}
