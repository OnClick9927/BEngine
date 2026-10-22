namespace BEngine.Platform;

public sealed record ProcessRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}
