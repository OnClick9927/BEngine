using BEngine.Editor.Codex;

namespace BEngine.ExampleTests.CodexCliDiscovery;

internal sealed class CodexCliDiscoveryTests
{
    private readonly string _hostExecutable = ResolveHostExecutable();

    public async Task RunAsync()
    {
        BEngineSkillCatalogTests.Run();
        await ConfiguredAbsoluteExecutableStartsAppServerAsync().ConfigureAwait(false);
        await PathExecutableStartsAppServerAsync().ConfigureAwait(false);
        await PathCommandShimStartsAppServerAndPreservesArgumentsAsync().ConfigureAwait(false);
        await new CodexAppServerConversationTests(_hostExecutable).RunAsync().ConfigureAwait(false);
        await MissingExecutableErrorIsNotDuplicatedAcrossRestartsAsync().ConfigureAwait(false);
    }

    private async Task ConfiguredAbsoluteExecutableStartsAppServerAsync()
    {
        using var fixture = new CodexCliFixture("absolute");
        using var client = new CodexAppServerClient(fixture.ProjectRoot);
        client.Settings.ExecutablePath = _hostExecutable;

        await client.StartAsync().ConfigureAwait(false);

        AssertReady(client, "configured absolute executable");
    }

    private async Task PathExecutableStartsAppServerAsync()
    {
        using var fixture = new CodexCliFixture("path-exe");
        fixture.CopyHostAs("codex.exe", _hostExecutable);
        using var environment = CodexEnvironmentScope.WithSearchPath(fixture.BinRoot);
        using var client = new CodexAppServerClient(fixture.ProjectRoot);

        await client.StartAsync().ConfigureAwait(false);

        AssertReady(client, "PATH codex.exe");
    }

    private async Task PathCommandShimStartsAppServerAndPreservesArgumentsAsync()
    {
        using var fixture = new CodexCliFixture("path-cmd");
        var argumentsPath = Path.Combine(fixture.Root, "arguments.txt");
        fixture.WriteCommandShim("codex.cmd", _hostExecutable);
        using var environment = CodexEnvironmentScope.WithSearchPath(fixture.BinRoot, argumentsPath);
        using var client = new CodexAppServerClient(fixture.ProjectRoot);

        await client.StartAsync().ConfigureAwait(false);

        AssertReady(client, "PATH codex.cmd");
        TestAssert.True(File.Exists(argumentsPath), "The command shim did not launch the app server.");
        TestAssert.SequenceEqual(["app-server"], await File.ReadAllLinesAsync(argumentsPath).ConfigureAwait(false),
            "The Windows command shim did not receive the exact app-server argument.");
    }

    private static async Task MissingExecutableErrorIsNotDuplicatedAcrossRestartsAsync()
    {
        using var fixture = new CodexCliFixture("missing");
        var missingExecutable = Path.Combine(fixture.BinRoot, "missing-codex.exe");

        await StartMissingClientAsync(fixture.ProjectRoot, missingExecutable).ConfigureAwait(false);
        await StartMissingClientAsync(fixture.ProjectRoot, missingExecutable).ConfigureAwait(false);

        using var client = new CodexAppServerClient(fixture.ProjectRoot);
        var errors = client.GetSnapshot().Transcript
            .Where(entry => entry.Kind == CodexTranscriptKind.Error)
            .ToArray();
        TestAssert.Equal(1, errors.Length,
            "Restarting the Codex client persisted duplicate copies of the same launch error.");
        TestAssert.True(errors[0].Text.Contains("Codex", StringComparison.OrdinalIgnoreCase),
            "The persisted launch error is not actionable.");
    }

    private static async Task StartMissingClientAsync(string projectRoot, string executablePath)
    {
        using var client = new CodexAppServerClient(projectRoot);
        client.Settings.ExecutablePath = executablePath;
        await client.StartAsync().ConfigureAwait(false);
        TestAssert.Equal(CodexConnectionStatus.Faulted, client.GetSnapshot().ConnectionStatus,
            "A missing configured executable did not fault the client.");
    }

    private static void AssertReady(CodexAppServerClient client, string source)
    {
        var snapshot = client.GetSnapshot();
        TestAssert.Equal(CodexConnectionStatus.Ready, snapshot.ConnectionStatus,
            $"Codex did not reach Ready through {source}. Status: {snapshot.StatusText}");
        TestAssert.Equal("chatgpt", snapshot.AuthMode,
            $"The fake app server response was not received through {source}.");
    }

    private static string ResolveHostExecutable()
    {
        var path = Environment.ProcessPath;
        TestAssert.True(!string.IsNullOrWhiteSpace(path) && File.Exists(path),
            "The test app host executable could not be resolved.");
        TestAssert.True(!Path.GetFileName(path!).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase),
            "CodexCliDiscovery must run through its app host, not `dotnet <assembly>`. ");
        return Path.GetFullPath(path!);
    }
}
