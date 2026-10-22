namespace BEngine.Platform;

public interface IProcessHandle : IDisposable
{
    bool HasExited { get; }
    StreamWriter StandardInput { get; }
    StreamReader StandardOutput { get; }
    StreamReader StandardError { get; }
    void Kill(bool entireProcessTree = true);
}
