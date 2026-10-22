using System.Diagnostics;
using System.Text;

namespace BEngine.Platform;

public sealed class SystemProcessHost : IProcessHost
{
    public static SystemProcessHost Shared { get; } = new();

    public IProcessHandle Start(ProcessHostRequest request) =>
        new SystemProcessHandle(StartCore(request));

    public void StartDetached(ProcessHostRequest request)
    {
        using var process = StartCore(request);
    }

    private static Process StartCore(ProcessHostRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.UseShellExecute &&
            (request.RedirectStandardInput || request.RedirectStandardOutput || request.RedirectStandardError))
            throw new ArgumentException(
                "A shell-launched process cannot redirect standard streams.", nameof(request));
        if (!string.IsNullOrWhiteSpace(request.RawArguments) && request.Arguments.Count > 0)
            throw new ArgumentException(
                "RawArguments and Arguments cannot both be specified.", nameof(request));

        var startInfo = new ProcessStartInfo(request.FileName)
        {
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
            Arguments = request.RawArguments ?? string.Empty,
            UseShellExecute = request.UseShellExecute,
            RedirectStandardInput = request.RedirectStandardInput,
            RedirectStandardOutput = request.RedirectStandardOutput,
            RedirectStandardError = request.RedirectStandardError,
            CreateNoWindow = request.CreateNoWindow
        };
        if (request.RedirectStandardInput) startInfo.StandardInputEncoding = new UTF8Encoding(false);
        if (request.RedirectStandardOutput) startInfo.StandardOutputEncoding = new UTF8Encoding(false);
        if (request.RedirectStandardError) startInfo.StandardErrorEncoding = new UTF8Encoding(false);
        foreach (var argument in request.Arguments) startInfo.ArgumentList.Add(argument);
        foreach (var (name, value) in request.EnvironmentVariables)
            startInfo.Environment[name] = value;
        return Process.Start(startInfo) ?? throw new InvalidOperationException(
            $"Could not start process '{request.FileName}'.");
    }
}
