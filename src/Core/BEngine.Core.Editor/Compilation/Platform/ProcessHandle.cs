namespace BEngine.Platform;

/// <summary>Application-facing API; the platform host supplies IProcessHandle through dependency injection.</summary>
public sealed class ProcessHandle : IDisposable
{
    private readonly IProcessHandle _implementation;

    public ProcessHandle(IProcessHandle implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public bool HasExited => _implementation.HasExited;

    public StreamWriter StandardInput => _implementation.StandardInput;

    public StreamReader StandardOutput => _implementation.StandardOutput;

    public StreamReader StandardError => _implementation.StandardError;

    public void Kill(bool entireProcessTree = true) =>
        _implementation.Kill(entireProcessTree);
    public void Dispose() => _implementation.Dispose();
}
