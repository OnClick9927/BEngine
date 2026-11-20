using System.Diagnostics;

namespace BEngine.Platform;

public sealed class SystemProcessRunner : IProcessRunner
{
    public static SystemProcessRunner Shared { get; } = new();

    public ProcessRunResult Run(
        ProcessRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Start(request);
        var standardOutput = request.RedirectStandardOutput
            ? process.StandardOutput.ReadToEndAsync(cancellationToken)
            : Task.FromResult(string.Empty);
        var standardError = request.RedirectStandardError
            ? process.StandardError.ReadToEndAsync(cancellationToken)
            : Task.FromResult(string.Empty);
        using var cancellationRegistration = RegisterCancellation(process, cancellationToken);
        process.WaitForExit();
        cancellationToken.ThrowIfCancellationRequested();
        return new ProcessRunResult(
            process.ExitCode,
            standardOutput.GetAwaiter().GetResult(),
            standardError.GetAwaiter().GetResult());
    }

    public async Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Start(request);
        var standardOutput = request.RedirectStandardOutput
            ? process.StandardOutput.ReadToEndAsync(cancellationToken)
            : Task.FromResult(string.Empty);
        var standardError = request.RedirectStandardError
            ? process.StandardError.ReadToEndAsync(cancellationToken)
            : Task.FromResult(string.Empty);
        using var cancellationRegistration = RegisterCancellation(process, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        return new ProcessRunResult(
            process.ExitCode,
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false));
    }

    private static Process Start(ProcessRunRequest request)
    {
        var startInfo = new ProcessStartInfo(request.FileName)
        {
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
            UseShellExecute = request.UseShellExecute,
            RedirectStandardOutput = request.RedirectStandardOutput,
            RedirectStandardError = request.RedirectStandardError,
            CreateNoWindow = request.CreateNoWindow
        };
        foreach (var argument in request.Arguments) startInfo.ArgumentList.Add(argument);
        foreach (var (name, value) in request.EnvironmentVariables)
            startInfo.Environment[name] = value;
        return Process.Start(startInfo) ?? throw new InvalidOperationException(
            $"Could not start process '{request.FileName}'.");
    }

    private static CancellationTokenRegistration RegisterCancellation(
        Process process,
        CancellationToken cancellationToken) => cancellationToken.Register(() =>
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    });
}
