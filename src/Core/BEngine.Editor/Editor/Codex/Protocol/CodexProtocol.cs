using System.Text;
using System.Text.Json;

namespace BEngine.Editor.Codex;

public static class CodexProtocol
{
    public static CodexProtocolEvent ParseEvent(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var method = ReadString(root, "method");
        var parameters = root.TryGetProperty("params", out var paramsElement) ? paramsElement : default;

        return method switch
        {
            "item/agentMessage/delta" => new CodexProtocolEvent(
                CodexProtocolEventKind.AgentDelta, method, ReadString(parameters, "itemId"),
                ReadString(parameters, "turnId"), ReadString(parameters, "delta")),
            "item/completed" => ParseCompletedItem(method, parameters),
            "item/started" => ParseStartedItem(method, parameters),
            "turn/started" => new CodexProtocolEvent(
                CodexProtocolEventKind.TurnStarted, method,
                TurnId: ReadNestedString(parameters, "turn", "id"), Status: "inProgress"),
            "turn/completed" => ParseTurnCompleted(method, parameters),
            "turn/plan/updated" => new CodexProtocolEvent(
                CodexProtocolEventKind.PlanUpdated, method,
                TurnId: ReadString(parameters, "turnId"), Text: ReadPlan(parameters)),
            "error" => new CodexProtocolEvent(
                CodexProtocolEventKind.Error, method, Text: ReadError(parameters)),
            "warning" or "configWarning" => new CodexProtocolEvent(
                CodexProtocolEventKind.Warning, method,
                Text: ReadString(parameters, "message", ReadString(parameters, "summary"))),
            "account/updated" => new CodexProtocolEvent(
                CodexProtocolEventKind.AccountUpdated, method,
                AuthMode: ReadString(parameters, "authMode"), PlanType: ReadString(parameters, "planType")),
            "account/login/completed" => new CodexProtocolEvent(
                CodexProtocolEventKind.LoginCompleted, method,
                Text: ReadString(parameters, "error"), Status: ReadBoolean(parameters, "success") ? "completed" : "failed"),
            "item/commandExecution/requestApproval" => ParseApproval(root, parameters, CodexApprovalKind.Command),
            "item/fileChange/requestApproval" => ParseApproval(root, parameters, CodexApprovalKind.FileChange),
            _ => new CodexProtocolEvent(CodexProtocolEventKind.Unknown, method)
        };
    }

    public static string BuildApprovalResponse(string requestIdJson, string decision)
    {
        if (decision is not ("accept" or "acceptForSession" or "decline" or "cancel"))
        {
            throw new ArgumentOutOfRangeException(nameof(decision));
        }
        using var idDocument = JsonDocument.Parse(requestIdJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("id");
            idDocument.RootElement.WriteTo(writer);
            writer.WritePropertyName("result");
            writer.WriteStartObject();
            writer.WriteString("decision", decision);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static CodexProtocolEvent ParseStartedItem(string method, JsonElement parameters)
    {
        if (!parameters.TryGetProperty("item", out var item))
        {
            return new CodexProtocolEvent(CodexProtocolEventKind.Unknown, method);
        }
        var type = ReadString(item, "type");
        var itemId = ReadString(item, "id");
        var text = type switch
        {
            "commandExecution" => ReadCommand(item),
            "fileChange" => ReadFileChanges(item),
            "mcpToolCall" => $"{ReadString(item, "server")}.{ReadString(item, "tool")}",
            "webSearch" => $"Search: {ReadString(item, "query")}",
            _ => string.Empty
        };
        return string.IsNullOrWhiteSpace(text)
            ? new CodexProtocolEvent(CodexProtocolEventKind.Unknown, method, itemId)
            : new CodexProtocolEvent(CodexProtocolEventKind.ActivityStarted, method, itemId,
                Text: text, Status: ReadString(item, "status", "inProgress"));
    }

    private static CodexProtocolEvent ParseCompletedItem(string method, JsonElement parameters)
    {
        if (!parameters.TryGetProperty("item", out var item))
        {
            return new CodexProtocolEvent(CodexProtocolEventKind.Unknown, method);
        }
        var type = ReadString(item, "type");
        var itemId = ReadString(item, "id");
        if (type == "agentMessage")
        {
            return new CodexProtocolEvent(CodexProtocolEventKind.AgentCompleted, method, itemId,
                Text: ReadString(item, "text"), Status: "completed");
        }

        var text = type switch
        {
            "commandExecution" => JoinDetails(ReadCommand(item), ReadString(item, "aggregatedOutput")),
            "fileChange" => ReadFileChanges(item),
            "mcpToolCall" => $"{ReadString(item, "server")}.{ReadString(item, "tool")}",
            "webSearch" => $"Search: {ReadString(item, "query")}",
            "contextCompaction" => "Conversation context compacted",
            _ => string.Empty
        };
        return string.IsNullOrWhiteSpace(text)
            ? new CodexProtocolEvent(CodexProtocolEventKind.Unknown, method, itemId)
            : new CodexProtocolEvent(CodexProtocolEventKind.ActivityCompleted, method, itemId,
                Text: text, Status: ReadString(item, "status", "completed"));
    }

    private static CodexProtocolEvent ParseTurnCompleted(string method, JsonElement parameters)
    {
        if (!parameters.TryGetProperty("turn", out var turn))
        {
            return new CodexProtocolEvent(CodexProtocolEventKind.TurnCompleted, method);
        }
        var error = turn.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.Object
            ? ReadString(errorElement, "message")
            : string.Empty;
        return new CodexProtocolEvent(CodexProtocolEventKind.TurnCompleted, method,
            TurnId: ReadString(turn, "id"), Text: error, Status: ReadString(turn, "status"));
    }

    private static CodexProtocolEvent ParseApproval(
        JsonElement root, JsonElement parameters, CodexApprovalKind kind)
    {
        var command = parameters.TryGetProperty("command", out var commandElement)
            ? commandElement.ValueKind == JsonValueKind.Array
                ? string.Join(" ", commandElement.EnumerateArray().Select(item => item.ToString()))
                : commandElement.ToString()
            : string.Empty;
        var approval = new CodexApprovalRequest
        {
            RequestIdJson = root.GetProperty("id").GetRawText(),
            Kind = kind,
            ItemId = ReadString(parameters, "itemId"),
            Reason = ReadString(parameters, "reason"),
            Command = command,
            WorkingDirectory = ReadString(parameters, "cwd")
        };
        return new CodexProtocolEvent(CodexProtocolEventKind.ApprovalRequested,
            ReadString(root, "method"), approval.ItemId, Approval: approval);
    }

    private static string ReadCommand(JsonElement item)
    {
        if (!item.TryGetProperty("command", out var command)) return string.Empty;
        return command.ValueKind == JsonValueKind.Array
            ? string.Join(" ", command.EnumerateArray().Select(part => part.ToString()))
            : command.ToString();
    }

    private static string ReadFileChanges(JsonElement item)
    {
        if (!item.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
        {
            return "File changes";
        }
        return string.Join(Environment.NewLine, changes.EnumerateArray().Select(change =>
            $"{ReadString(change, "kind", "update")}: {ReadString(change, "path")}"));
    }

    private static string ReadPlan(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("plan", out var plan) || plan.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }
        return string.Join(Environment.NewLine, plan.EnumerateArray().Select(item =>
            $"[{ReadString(item, "status", "pending")}] {ReadString(item, "step")}"));
    }

    private static string ReadError(JsonElement parameters)
    {
        if (parameters.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.Object) return ReadString(error, "message", error.ToString());
            return error.ToString();
        }
        return ReadString(parameters, "message", "Codex request failed.");
    }

    private static string JoinDetails(string title, string details)
    {
        details = details.Trim();
        const int maximumOutput = 4000;
        if (details.Length > maximumOutput) details = details[..maximumOutput] + Environment.NewLine + "...";
        return string.IsNullOrWhiteSpace(details) ? title : $"{title}{Environment.NewLine}{details}";
    }

    private static string ReadNestedString(JsonElement element, string property, string nestedProperty) =>
        element.TryGetProperty(property, out var nested) ? ReadString(nested, nestedProperty) : string.Empty;

    private static string ReadString(JsonElement element, string property, string fallback = "") =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value.ToString()
            : fallback;

    private static bool ReadBoolean(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.True;
}
