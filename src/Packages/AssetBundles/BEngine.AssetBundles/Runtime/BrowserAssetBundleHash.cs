using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using BEngine.Networking;

namespace BEngine.AssetBundles;

// Optional platform backend: no native handle or unverified result escapes the owning scheduler.
internal static class BrowserAssetBundleHash
{
    internal const int MaximumInputBytes = 64 * 1024 * 1024;

    internal static bool IsSupported(long size)
    {
        if (!OperatingSystem.IsBrowser() || size < 0 || size > MaximumInputBytes) return false;
        return IsSupported(size, NativeBridge.Shared);
    }

    internal static bool IsSupported(long size, IBrowserAssetBundleHashBridge bridge)
    {
        if (size < 0 || size > MaximumInputBytes) return false;
        try { return bridge.Available(); }
        catch (JSException) { return false; } // An older host retains managed incremental SHA.
    }

    internal static BValueTask VerifyAsync(byte[] bytes, string expectedSha256, BCancellationToken token)
    {
        if (!OperatingSystem.IsBrowser()) throw new PlatformNotSupportedException("Native bundle SHA requires a browser.");
        return VerifyAsync(bytes, expectedSha256, token, NativeBridge.Shared);
    }

    internal static async BValueTask VerifyAsync(byte[] bytes, string expectedSha256,
        BCancellationToken token, IBrowserAssetBundleHashBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(bridge);
        if (!IsSupported(bytes.LongLength, bridge))
            throw new PlatformNotSupportedException("Native bundle SHA is unavailable for this payload.");
        Operation? operation;
        do
        {
            token.ThrowIfCancellationRequested();
            operation = TryBegin(bytes, expectedSha256, bridge);
            if (operation is null) await BTask.Yield(); // Native input admission remains charged until digest settles.
        } while (operation is null);
        using (operation)
        using (token.Register(operation.Dispose))
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                operation.Poll();
                if (operation.Status == NetworkOperationStatus.Succeeded) return;
                if (operation.Status == NetworkOperationStatus.Failed)
                    throw operation.Exception ?? new IOException("Native bundle SHA failed.");
                await BTask.Yield();
            }
        }
    }

    // null only means admission was declined before accepting/copying input. Accepted failures never fall back.
    internal static Operation? TryBegin(byte[] bytes, string expectedSha256, IBrowserAssetBundleHashBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(bridge);
        AssetBundleValidation.ValidateSha256(expectedSha256, nameof(expectedSha256));
        if (bytes.LongLength > MaximumInputBytes) return null;
        var operation = new Operation(bytes, expectedSha256, bridge);
        return operation.HasHandle || operation.Status == NetworkOperationStatus.Failed ? operation : null;
    }

    internal sealed class Operation : IDisposable
    {
        private readonly IBrowserAssetBundleHashBridge _bridge;
        private readonly string _expected;
        private int _handle;
        private bool _disposed;
        internal Operation(byte[] bytes, string expected, IBrowserAssetBundleHashBridge bridge)
        {
            _bridge = bridge; _expected = expected;
            try
            {
                _handle = bridge.Begin(bytes);
                if (_handle < 0) throw new InvalidDataException("Native SHA returned an invalid handle.");
            }
            catch (Exception exception) { Exception = exception; Status = NetworkOperationStatus.Failed; }
        }
        internal bool HasHandle => _handle > 0;
        internal NetworkOperationStatus Status { get; private set; } = NetworkOperationStatus.Running;
        internal Exception? Exception { get; private set; }
        internal void Poll()
        {
            if (_disposed || Status != NetworkOperationStatus.Running) return;
            try
            {
                var status = _bridge.Status(_handle);
                if (status == 0) return;
                if (status == -1) throw new IOException(_bridge.Error(_handle) ?? "Native SHA failed.");
                if (status != 1) throw new InvalidDataException("Native SHA returned an invalid status.");
                var result = _bridge.Result(_handle);
                AssetBundleValidation.ValidateSha256(result!, "native SHA result");
                if (!string.Equals(result, _expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("HTTP bundle failed native SHA-256 verification.");
                Status = NetworkOperationStatus.Succeeded;
            }
            catch (Exception exception) { Exception = exception; Status = NetworkOperationStatus.Failed; }
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Status == NetworkOperationStatus.Running)
            {
                Exception = new IOException("Native bundle SHA was released.");
                Status = NetworkOperationStatus.Failed;
            }
            var handle = _handle; _handle = 0;
            if (handle > 0) try { _bridge.Release(handle); } catch { }
        }
    }

    [SupportedOSPlatform("browser")]
    private sealed class NativeBridge : IBrowserAssetBundleHashBridge
    {
        internal static NativeBridge Shared { get; } = new();
        public bool Available() => BrowserAssetBundleHashInterop.Available();
        public int Begin(Span<byte> bytes) => BrowserAssetBundleHashInterop.Begin(bytes);
        public int Status(int handle) => BrowserAssetBundleHashInterop.Status(handle);
        public string? Result(int handle) => BrowserAssetBundleHashInterop.Result(handle);
        public string? Error(int handle) => BrowserAssetBundleHashInterop.Error(handle);
        public void Release(int handle) => BrowserAssetBundleHashInterop.Release(handle);
    }
}

internal interface IBrowserAssetBundleHashBridge
{
    bool Available();
    int Begin(Span<byte> bytes);
    int Status(int handle);
    string? Result(int handle);
    string? Error(int handle);
    void Release(int handle);
}

[SupportedOSPlatform("browser")]
internal static partial class BrowserAssetBundleHashInterop
{
    [JSImport("globalThis.BEngineWebAssetBundleHash.available")]
    internal static partial bool Available();
    [JSImport("globalThis.BEngineWebAssetBundleHash.begin")]
    internal static partial int Begin([JSMarshalAs<JSType.MemoryView>] Span<byte> bytes);
    [JSImport("globalThis.BEngineWebAssetBundleHash.status")]
    internal static partial int Status(int handle);
    [JSImport("globalThis.BEngineWebAssetBundleHash.result")]
    internal static partial string? Result(int handle);
    [JSImport("globalThis.BEngineWebAssetBundleHash.error")]
    internal static partial string? Error(int handle);
    [JSImport("globalThis.BEngineWebAssetBundleHash.release")]
    internal static partial void Release(int handle);
}
