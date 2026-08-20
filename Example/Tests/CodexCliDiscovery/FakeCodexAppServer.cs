using System.Text.Json;

namespace BEngine.ExampleTests.CodexCliDiscovery;

internal static class FakeCodexAppServer
{
    private const string ThreadId = "fake-thread-1";
    private const string FailureMessage = "Synthetic app-server failure.";

    public static async Task<int> RunAsync(string[] arguments)
    {
        var argumentsPath = Environment.GetEnvironmentVariable("BENGINE_CODEX_TEST_ARGUMENTS_PATH");
        if (!string.IsNullOrWhiteSpace(argumentsPath))
        {
            await File.WriteAllLinesAsync(argumentsPath, arguments).ConfigureAwait(false);
        }

        var protocolPath = Environment.GetEnvironmentVariable("BENGINE_CODEX_TEST_PROTOCOL_PATH");
        var turnNumber = 0;
        while (await Console.In.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(protocolPath))
                await File.AppendAllTextAsync(protocolPath, line + Environment.NewLine).ConfigureAwait(false);
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var id))
            {
                continue;
            }

            var method = root.TryGetProperty("method", out var methodElement)
                ? methodElement.GetString()
                : string.Empty;
            switch (method)
            {
                case "account/read":
                    await WriteResultAsync(id,
                        new
                        {
                            account = new { type = "chatgpt", planType = "test" },
                            requiresOpenaiAuth = false
                        }).ConfigureAwait(false);
                    break;
                case "thread/start":
                    await WriteResultAsync(id, new { thread = new { id = ThreadId } }).ConfigureAwait(false);
                    break;
                case "turn/start":
                    turnNumber++;
                    var turnId = $"fake-turn-{turnNumber}";
                    var prompt = ReadPrompt(root);
                    await WriteResultAsync(id, new { turn = new { id = turnId } }).ConfigureAwait(false);
                    await Task.Delay(50).ConfigureAwait(false);
                    if (prompt.Contains("TRIGGER_ERROR", StringComparison.Ordinal))
                        await WriteFailedTurnAsync(turnId).ConfigureAwait(false);
                    else
                        await WriteSuccessfulTurnAsync(turnId).ConfigureAwait(false);
                    break;
                default:
                    await WriteResultAsync(id, new { }).ConfigureAwait(false);
                    break;
            }
        }

        return 0;
    }

    private static string ReadPrompt(JsonElement request)
    {
        if (!request.TryGetProperty("params", out var parameters) ||
            !parameters.TryGetProperty("input", out var input) ||
            input.ValueKind != JsonValueKind.Array || input.GetArrayLength() == 0)
            return string.Empty;
        var first = input[0];
        return first.TryGetProperty("text", out var text) ? text.GetString() ?? string.Empty : string.Empty;
    }

    private static async Task WriteSuccessfulTurnAsync(string turnId)
    {
        const string itemId = "assistant-1";
        await WriteMessageAsync(new
        {
            method = "turn/started",
            @params = new { turn = new { id = turnId, status = "inProgress" } }
        }).ConfigureAwait(false);
        await WriteMessageAsync(new
        {
            method = "item/agentMessage/delta",
            @params = new { itemId, turnId, delta = "Hello from " }
        }).ConfigureAwait(false);
        await WriteMessageAsync(new
        {
            method = "item/agentMessage/delta",
            @params = new { itemId, turnId, delta = "fake Codex." }
        }).ConfigureAwait(false);
        await WriteMessageAsync(new
        {
            method = "item/completed",
            @params = new
            {
                item = new { id = itemId, type = "agentMessage", text = "Hello from fake Codex." }
            }
        }).ConfigureAwait(false);
        await WriteMessageAsync(new
        {
            method = "turn/completed",
            @params = new { turn = new { id = turnId, status = "completed" } }
        }).ConfigureAwait(false);
    }

    private static async Task WriteFailedTurnAsync(string turnId)
    {
        await WriteMessageAsync(new
        {
            method = "turn/started",
            @params = new { turn = new { id = turnId, status = "inProgress" } }
        }).ConfigureAwait(false);
        await WriteMessageAsync(new
        {
            method = "error",
            @params = new { error = new { message = FailureMessage } }
        }).ConfigureAwait(false);
        await WriteMessageAsync(new
        {
            method = "turn/completed",
            @params = new
            {
                turn = new { id = turnId, status = "failed", error = new { message = FailureMessage } }
            }
        }).ConfigureAwait(false);
    }

    private static Task WriteResultAsync(JsonElement id, object result) =>
        WriteLineAsync($"{{\"id\":{id.GetRawText()},\"result\":{JsonSerializer.Serialize(result)}}}");

    private static Task WriteMessageAsync(object message) =>
        WriteLineAsync(JsonSerializer.Serialize(message));

    private static async Task WriteLineAsync(string line)
    {
        await Console.Out.WriteLineAsync(line).ConfigureAwait(false);
        await Console.Out.FlushAsync().ConfigureAwait(false);
    }
}
