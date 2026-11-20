namespace BEngine.Platform;

/// <summary>Application-facing API; the platform host supplies IProcessHost through dependency injection.</summary>
public sealed class ProcessHostUtility
{
    private readonly IProcessHost _implementation;

    public ProcessHostUtility(IProcessHost implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static ProcessHostUtility Current => BEngine.Editor.EditorServices.GetRequiredService<ProcessHostUtility>();

    public ProcessHandle Start(ProcessHostRequest request) =>
        new(_implementation.Start(request));

    public void StartDetached(ProcessHostRequest request) =>
        _implementation.StartDetached(request);
}
