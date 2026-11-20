namespace BEngine.Platform;

public interface IProcessRunner
{
    ProcessRunResult Run(
        ProcessRunRequest request,
        CancellationToken cancellationToken = default);

    Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken = default);
}
