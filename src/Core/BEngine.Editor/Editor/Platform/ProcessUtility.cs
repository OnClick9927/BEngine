namespace BEngine.Platform;

/// <summary>Application-facing API; the platform host supplies IProcessRunner through dependency injection.</summary>
public sealed class ProcessUtility
{
    private readonly IProcessRunner _implementation;

    public ProcessUtility(IProcessRunner implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static ProcessUtility Current => BEngine.Editor.EditorServices.GetRequiredService<ProcessUtility>();

    public ProcessRunResult Run(ProcessRunRequest request, CancellationToken cancellationToken = default) =>
        _implementation.Run(request, cancellationToken);

    public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default) =>
        _implementation.RunAsync(request, cancellationToken);
}
