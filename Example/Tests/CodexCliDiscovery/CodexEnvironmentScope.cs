namespace BEngine.ExampleTests.CodexCliDiscovery;

internal sealed class CodexEnvironmentScope : IDisposable
{
    private readonly string? _path;
    private readonly string? _configuredPath;
    private readonly string? _argumentsPath;
    private readonly string? _protocolPath;
    private bool _disposed;

    private CodexEnvironmentScope(string? searchPath, string? argumentsPath, string? protocolPath)
    {
        _path = Environment.GetEnvironmentVariable("PATH");
        _configuredPath = Environment.GetEnvironmentVariable("BENGINE_CODEX_PATH");
        _argumentsPath = Environment.GetEnvironmentVariable("BENGINE_CODEX_TEST_ARGUMENTS_PATH");
        _protocolPath = Environment.GetEnvironmentVariable("BENGINE_CODEX_TEST_PROTOCOL_PATH");
        if (searchPath is not null) Environment.SetEnvironmentVariable("PATH", searchPath);
        Environment.SetEnvironmentVariable("BENGINE_CODEX_PATH", null);
        Environment.SetEnvironmentVariable("BENGINE_CODEX_TEST_ARGUMENTS_PATH", argumentsPath);
        Environment.SetEnvironmentVariable("BENGINE_CODEX_TEST_PROTOCOL_PATH", protocolPath);
    }

    public static CodexEnvironmentScope WithSearchPath(string searchPath, string? argumentsPath = null) =>
        new(searchPath, argumentsPath, null);

    public static CodexEnvironmentScope WithProtocolTrace(string protocolPath) =>
        new(null, null, protocolPath);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Environment.SetEnvironmentVariable("PATH", _path);
        Environment.SetEnvironmentVariable("BENGINE_CODEX_PATH", _configuredPath);
        Environment.SetEnvironmentVariable("BENGINE_CODEX_TEST_ARGUMENTS_PATH", _argumentsPath);
        Environment.SetEnvironmentVariable("BENGINE_CODEX_TEST_PROTOCOL_PATH", _protocolPath);
    }
}
