using System.Diagnostics;

namespace BEngine.Platform;

internal sealed class SystemProcessHandle : IProcessHandle
{
    private readonly Process _process;

    internal SystemProcessHandle(Process process)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
    }

    public bool HasExited => _process.HasExited;
    public StreamWriter StandardInput => _process.StandardInput;
    public StreamReader StandardOutput => _process.StandardOutput;
    public StreamReader StandardError => _process.StandardError;
    public void Kill(bool entireProcessTree = true) => _process.Kill(entireProcessTree);
    public void Dispose() => _process.Dispose();
}
