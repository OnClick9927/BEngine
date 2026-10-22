namespace BEngine.Platform;

public sealed class ProcessRunRequest
{
    public ProcessRunRequest(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        FileName = fileName;
    }

    public string FileName { get; }
    public string? WorkingDirectory { get; init; }
    public bool UseShellExecute { get; init; }
    public bool RedirectStandardOutput { get; init; } = true;
    public bool RedirectStandardError { get; init; } = true;
    public bool CreateNoWindow { get; init; } = true;
    public IList<string> Arguments { get; } = new List<string>();
    public IDictionary<string, string?> EnvironmentVariables { get; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}
