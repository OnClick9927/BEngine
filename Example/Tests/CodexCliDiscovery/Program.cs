namespace BEngine.ExampleTests.CodexCliDiscovery;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args is ["app-server"])
        {
            return await FakeCodexAppServer.RunAsync(args).ConfigureAwait(false);
        }

        if (args is ["--real-smoke"])
        {
            return await RunRealSmokeAsync().ConfigureAwait(false);
        }

        if (args is ["--real-conversation-smoke"])
        {
            return await RunRealConversationSmokeAsync().ConfigureAwait(false);
        }

        try
        {
            await new CodexCliDiscoveryTests().RunAsync().ConfigureAwait(false);
            Console.WriteLine("CODEX_CLI_DISCOVERY_OK|skills,absolute,path-exe,path-cmd,shim-args," +
                              "app-server-dialogue,assistant-stream,error-event,error-deduplication");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"CODEX_CLI_DISCOVERY_FAILED|{exception}");
            return 1;
        }
    }

    private static async Task<int> RunRealSmokeAsync()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "BEngine", "CodexCliRealSmoke", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(projectRoot);
            using var client = new BEngine.Editor.Codex.CodexAppServerClient(projectRoot);
            await client.StartAsync().ConfigureAwait(false);
            var snapshot = client.GetSnapshot();
            Console.WriteLine($"CODEX_CLI_REAL_SMOKE|{snapshot.ConnectionStatus}|{snapshot.StatusText}");
            return snapshot.ConnectionStatus == BEngine.Editor.Codex.CodexConnectionStatus.Ready ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"CODEX_CLI_REAL_SMOKE_FAILED|{exception}");
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<int> RunRealConversationSmokeAsync()
    {
        const string expected = "BENGINE_CODEX_SMOKE_OK";
        var projectRoot = Path.Combine(Path.GetTempPath(), "BEngine", "CodexConversationSmoke",
            Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(projectRoot);
            using var client = new BEngine.Editor.Codex.CodexAppServerClient(projectRoot);
            client.Settings.ApprovalPolicy = "never";
            client.Settings.NetworkAccess = false;
            await client.StartAsync().ConfigureAwait(false);
            var started = client.GetSnapshot();
            if (started.ConnectionStatus != BEngine.Editor.Codex.CodexConnectionStatus.Ready)
            {
                Console.Error.WriteLine($"CODEX_REAL_CONVERSATION_FAILED|{started.StatusText}");
                return 1;
            }

            var initialRevision = started.Revision;
            await client.SendTurnAsync($"Reply with exactly {expected}. Do not use tools or modify files.")
                .ConfigureAwait(false);
            var deadline = DateTime.UtcNow.AddMinutes(3);
            while (DateTime.UtcNow < deadline)
            {
                var snapshot = client.GetSnapshot();
                var response = snapshot.Transcript.LastOrDefault(entry =>
                    entry.Kind == BEngine.Editor.Codex.CodexTranscriptKind.Assistant &&
                    entry.Status == "completed");
                if (response is not null && !snapshot.IsTurnRunning)
                {
                    Console.WriteLine($"CODEX_REAL_CONVERSATION|{snapshot.ConnectionStatus}|{response.Text}");
                    return response.Text.Contains(expected, StringComparison.Ordinal) ? 0 : 1;
                }
                var error = snapshot.Transcript.LastOrDefault(entry =>
                    entry.Kind == BEngine.Editor.Codex.CodexTranscriptKind.Error &&
                    snapshot.Revision > initialRevision);
                if (error is not null && !snapshot.IsTurnRunning)
                {
                    Console.Error.WriteLine($"CODEX_REAL_CONVERSATION_FAILED|{error.Text}");
                    return 1;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            Console.Error.WriteLine("CODEX_REAL_CONVERSATION_FAILED|Timed out waiting for a completed response.");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"CODEX_REAL_CONVERSATION_FAILED|{exception}");
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
