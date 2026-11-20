namespace BEngine.Hosting;

/// <summary>
/// Owns early engine preparation and its host presentation. Application
/// workflows start after preparation; they never own the splash surface or its timing.
/// </summary>
public sealed class HostStartupSession : IDisposable
{
    private readonly HostRuntimeLifetime _runtimeLifetime;
    private readonly bool _hasLogo;
    private bool _closed;
    private bool _disposed;

    private HostStartupSession(HostStartupProgress progress, HostRuntimeLifetime runtimeLifetime)
    {
        Progress = progress;
        _runtimeLifetime = runtimeLifetime;
        _hasLogo = HostStartupEnvironment.LogoPng.Length != 0;
    }

    public static HostStartupSession? Current { get; private set; }
    public HostStartupProgress Progress { get; }
    public bool IsVisible => !_disposed && !_closed && _hasLogo;
    public bool CanContinue => _hasLogo ? Progress.CanContinue : Progress.IsPrepared;

    public static HostStartupSession Begin(
        TimeSpan elapsedBeforeStart = default,
        bool holdPresentation = false,
        bool allowRuntimeThreadHandoff = false)
    {
        if (Current is { _disposed: false })
            throw new InvalidOperationException("An engine startup session is already active.");
        var runtimeLifetime = HostRuntimeLifetime.Begin(allowRuntimeThreadHandoff);
        try
        {
            var progress = HostStartupEnvironment.Begin(elapsedBeforeStart);
            if (holdPresentation) progress.HoldPresentation();
            return Current = new HostStartupSession(progress, runtimeLifetime);
        }
        catch { runtimeLifetime.Dispose(); throw; }
    }

    public void Pump()
    {
        // Presentation is pumped by the application frame, never a separate window.
    }

    public void CompletePreparation() => Progress.Complete();

    public async BValueTask WaitUntilReadyAsync(BCancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (!CanContinue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Pump();
            await BTask.Delay(8, cancellationToken);
        }
    }

    public async BValueTask CloseWhenReadyAsync(BCancellationToken cancellationToken = default)
    {
        await WaitUntilReadyAsync(cancellationToken);
        Close();
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Close(); }
        finally
        {
            _runtimeLifetime.Dispose();
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
}
