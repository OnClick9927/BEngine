using BEngine.Networking;
using BEngine.Platform;

namespace BEngine.AssetBundles;

// Resource policy: the platform supplies a digest, while AB owns expected hashes,
// cooperative scheduling and cancellation. Native handles stay inside the host.
internal static class AssetBundleHashVerifier
{
    internal static bool IsSupported(long size) => size >= 0 && PlatformRuntime.GetService<IPlatformSha256Service>()?.CanComputeAsync(size) == true;
    internal static bool IsSupported(long size, IPlatformSha256Service service) => size >= 0 && service.CanComputeAsync(size);

    internal static BValueTask VerifyAsync(byte[] bytes, string expectedSha256, BCancellationToken token)
    {
        return VerifyAsync(bytes, expectedSha256, token, PlatformRuntime.GetRequiredService<IPlatformSha256Service>());
    }

    internal static async BValueTask VerifyAsync(byte[] bytes, string expectedSha256,
        BCancellationToken token, IPlatformSha256Service service)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(service);
        if (!IsSupported(bytes.LongLength, service))
            throw new PlatformNotSupportedException("Native bundle SHA is unavailable for this payload.");
        Operation? operation;
        do
        {
            token.ThrowIfCancellationRequested();
            operation = TryBegin(bytes, expectedSha256, service);
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
    internal static Operation? TryBegin(byte[] bytes, string expectedSha256, IPlatformSha256Service service)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(service);
        AssetBundleValidation.ValidateSha256(expectedSha256, nameof(expectedSha256));
        if (!IsSupported(bytes.LongLength, service)) return null;
        var operation = new Operation(bytes, expectedSha256, service);
        return operation.HasLease || operation.Status == NetworkOperationStatus.Failed ? operation : null;
    }

    internal sealed class Operation : IDisposable
    {
        private readonly string _expected;
        private IPlatformSha256Operation? _operation;
        private bool _disposed;
        internal Operation(byte[] bytes, string expected, IPlatformSha256Service service)
        {
            _expected = expected;
            try
            {
                _operation = service.TryBegin(bytes);
            }
            catch (Exception exception) { Exception = exception; Status = NetworkOperationStatus.Failed; }
        }
        internal bool HasLease => _operation is not null;
        internal NetworkOperationStatus Status { get; private set; } = NetworkOperationStatus.Running;
        internal Exception? Exception { get; private set; }
        internal void Poll()
        {
            if (_disposed || Status != NetworkOperationStatus.Running) return;
            try
            {
                if (_operation is null) throw new InvalidOperationException("No digest lease was admitted.");
                _operation.Poll();
                var status = _operation.Status;
                if (status == PlatformHashOperationStatus.Running) return;
                if (status == PlatformHashOperationStatus.Failed) throw _operation.Exception ?? new IOException("Native SHA failed.");
                if (status != PlatformHashOperationStatus.Succeeded) throw new InvalidDataException("Native SHA returned an invalid status.");
                var result = _operation.Result;
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
            var operation = _operation; _operation = null;
            if (operation is not null) try { operation.Dispose(); } catch { }
        }
    }

}
