namespace BEngine.Codex.Editor;

public enum CodexConnectionStatus
{
    Stopped,
    Starting,
    Ready,
    Faulted
}

public enum CodexTranscriptKind
{
    User,
    Assistant,
    Activity,
    Plan,
    Error
}

public enum CodexApprovalKind
{
    Command,
    FileChange
}

public sealed class CodexTranscriptEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public CodexTranscriptKind Kind { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    internal CodexTranscriptEntry Copy() => new()
    {
        Id = Id,
        Kind = Kind,
        Text = Text,
        Status = Status,
        CreatedAt = CreatedAt
    };
}

public sealed class CodexApprovalRequest
{
    public string RequestIdJson { get; init; } = string.Empty;
    public CodexApprovalKind Kind { get; init; }
    public string ItemId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
}

public sealed class CodexProjectSettingsDocument
{
    public string Format { get; set; } = "BEngine.CodexSettings";
    public int Version { get; set; } = 1;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Effort { get; set; } = "medium";
    public string ApprovalPolicy { get; set; } = "unlessTrusted";
    public bool NetworkAccess { get; set; } = true;
}

public sealed class CodexSessionDocument
{
    public string Format { get; set; } = "BEngine.CodexSession";
    public int Version { get; set; } = 1;
    public string ThreadId { get; set; } = string.Empty;
    public List<CodexTranscriptEntry> Transcript { get; set; } = [];
}

public sealed record CodexClientSnapshot(
    CodexConnectionStatus ConnectionStatus,
    string StatusText,
    string ThreadId,
    string ActiveTurnId,
    bool IsTurnRunning,
    string AuthMode,
    string PlanType,
    string LoginUrl,
    IReadOnlyList<CodexTranscriptEntry> Transcript,
    IReadOnlyList<CodexApprovalRequest> Approvals,
    long Revision);
