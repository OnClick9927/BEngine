using System.Text.Json;
using BEngine.Editor.Codex;

namespace BEngine.ExampleTests.CodexCliDiscovery;

internal sealed class CodexAppServerConversationTests(string hostExecutable)
{
    private const string SuccessPrompt = "Reply through the fake app server";
    private const string FailurePrompt = "TRIGGER_ERROR";
    private const string FailureMessage = "Synthetic app-server failure.";

    public async Task RunAsync()
    {
        using var fixture = new CodexCliFixture("conversation");
        var protocolPath = Path.Combine(fixture.Root, "protocol.jsonl");
        WriteLegacySettings(fixture.ProjectRoot, "unlessTrusted");
        using var environment = CodexEnvironmentScope.WithProtocolTrace(protocolPath);
        using var client = new CodexAppServerClient(fixture.ProjectRoot);
        client.Settings.ExecutablePath = hostExecutable;
        TestAssert.Equal("untrusted", client.Settings.ApprovalPolicy,
            "Legacy unlessTrusted settings were not migrated before starting Codex.");
        var changedCount = 0;
        client.changed += () => Interlocked.Increment(ref changedCount);

        await client.StartAsync().ConfigureAwait(false);
        var started = client.GetSnapshot();
        TestAssert.Equal(CodexConnectionStatus.Ready, started.ConnectionStatus,
            "The dialogue fake server did not complete startup.");
        TestAssert.True(string.IsNullOrWhiteSpace(started.ThreadId),
            "Codex created a thread before the first user input.");

        await client.SendTurnAsync(SuccessPrompt).ConfigureAwait(false);
        var completed = await WaitForAsync(client, snapshot =>
            !snapshot.IsTurnRunning && snapshot.Transcript.Any(entry =>
                entry.Id == "assistant-1" && entry.Status == "completed"),
            "the streamed assistant response to complete").ConfigureAwait(false);
        TestAssert.Equal("fake-thread-1", completed.ThreadId,
            "The thread/start response was not retained by the client.");
        TestAssert.True(completed.Transcript.Any(entry =>
                entry.Kind == CodexTranscriptKind.User && entry.Text == SuccessPrompt &&
                entry.Status == "completed"),
            "The exact user input was not added to the transcript.");
        var assistant = completed.Transcript.Single(entry => entry.Id == "assistant-1");
        TestAssert.Equal(CodexTranscriptKind.Assistant, assistant.Kind,
            "Agent message deltas did not create an assistant transcript entry.");
        TestAssert.Equal("Hello from fake Codex.", assistant.Text,
            "Assistant delta/completion handling lost or duplicated streamed text.");
        TestAssert.Equal("Ready", completed.StatusText,
            "A completed turn did not restore the Ready status.");

        await client.SendTurnAsync(FailurePrompt).ConfigureAwait(false);
        var failed = await WaitForAsync(client, snapshot =>
            !snapshot.IsTurnRunning && snapshot.StatusText == "Turn failed" &&
            snapshot.Transcript.Any(entry => entry.Kind == CodexTranscriptKind.Error &&
                                             entry.Text == FailureMessage),
            "the failed turn event to be recorded").ConfigureAwait(false);
        TestAssert.Equal(1, failed.Transcript.Count(entry =>
                entry.Kind == CodexTranscriptKind.Error && entry.Text == FailureMessage),
            "The error event and failed completion produced duplicate errors.");
        TestAssert.True(failed.Transcript.Any(entry =>
                entry.Kind == CodexTranscriptKind.User && entry.Text == FailurePrompt),
            "The user input for the failed turn was not retained.");
        TestAssert.True(changedCount > 0, "App-server events did not notify the editor window.");

        VerifyRequests(protocolPath, fixture.ProjectRoot);
        VerifyOnRequestMigration();
    }

    private static void VerifyRequests(string protocolPath, string projectRoot)
    {
        TestAssert.True(File.Exists(protocolPath), "The fake app server did not receive any protocol messages.");
        var messages = File.ReadAllLines(protocolPath).Select(Parse).ToArray();
        TestAssert.True(messages.Any(message => Method(message) == "initialize"),
            "The client did not initialize the app server.");
        var threadStarts = messages.Where(message => Method(message) == "thread/start").ToArray();
        TestAssert.Equal(1, threadStarts.Length,
            "The client did not reuse its thread for the second turn.");
        var threadParameters = threadStarts[0].GetProperty("params");
        TestAssert.Equal("untrusted", threadParameters.GetProperty("approvalPolicy").GetString()!,
            "thread/start used an obsolete approval policy value.");
        TestAssert.Equal("workspace-write", threadParameters.GetProperty("sandbox").GetString()!,
            "thread/start used the turn policy discriminator instead of the SandboxMode wire value.");
        var turns = messages.Where(message => Method(message) == "turn/start").ToArray();
        TestAssert.Equal(2, turns.Length, "The client did not send both user turns.");
        foreach (var turn in turns)
        {
            var parameters = turn.GetProperty("params");
            TestAssert.Equal("fake-thread-1", parameters.GetProperty("threadId").GetString()!,
                "turn/start referenced the wrong thread.");
            TestAssert.Equal("untrusted", parameters.GetProperty("approvalPolicy").GetString()!,
                "turn/start used an obsolete approval policy value.");
            var sandbox = parameters.GetProperty("sandboxPolicy");
            TestAssert.Equal("workspaceWrite", sandbox.GetProperty("type").GetString()!,
                "turn/start used the ThreadStart SandboxMode value instead of the policy discriminator.");
            TestAssert.True(!sandbox.GetProperty("excludeTmpdirEnvVar").GetBoolean() &&
                            !sandbox.GetProperty("excludeSlashTmp").GetBoolean(),
                "turn/start omitted the explicit temporary-directory sandbox policy.");
            TestAssert.True(sandbox.GetProperty("writableRoots").EnumerateArray().Any(root =>
                    Path.GetFullPath(root.GetString()!).Equals(Path.GetFullPath(projectRoot),
                        StringComparison.OrdinalIgnoreCase)),
                "turn/start did not grant workspace access to the project root.");
        }
        TestAssert.True(ReadInput(turns[0]).StartsWith(SuccessPrompt, StringComparison.Ordinal),
            "The first turn/start request did not contain the user input.");
        TestAssert.True(ReadInput(turns[1]).StartsWith(FailurePrompt, StringComparison.Ordinal),
            "The failed turn/start request did not contain the user input.");
    }

    private static void VerifyOnRequestMigration()
    {
        using var fixture = new CodexCliFixture("on-request-migration");
        WriteLegacySettings(fixture.ProjectRoot, "onRequest");
        var store = new CodexProjectStore(fixture.ProjectRoot);
        var settings = store.LoadSettings();
        TestAssert.Equal("on-request", settings.ApprovalPolicy,
            "Legacy onRequest settings were not migrated to the current wire value.");
        var persisted = new CodexProjectStore(fixture.ProjectRoot).LoadSettings();
        TestAssert.Equal("on-request", persisted.ApprovalPolicy,
            "The migrated approval policy was not persisted.");
    }

    private static void WriteLegacySettings(string projectRoot, string approvalPolicy)
    {
        var store = new CodexProjectStore(projectRoot);
        new CodexProjectSettingsData { ApprovalPolicy = approvalPolicy }.Save(store.SettingsPath);
    }

    private static async Task<CodexClientSnapshot> WaitForAsync(
        CodexAppServerClient client,
        Func<CodexClientSnapshot, bool> predicate,
        string operation)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = client.GetSnapshot();
            if (predicate(snapshot)) return snapshot;
            await Task.Delay(10).ConfigureAwait(false);
        }
        var final = client.GetSnapshot();
        throw new TimeoutException($"Timed out waiting for {operation}. " +
                                   $"Status: {final.ConnectionStatus}/{final.StatusText}.");
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string Method(JsonElement message) =>
        message.TryGetProperty("method", out var method) ? method.GetString() ?? string.Empty : string.Empty;

    private static string ReadInput(JsonElement turn) =>
        turn.GetProperty("params").GetProperty("input")[0].GetProperty("text").GetString() ?? string.Empty;
}
