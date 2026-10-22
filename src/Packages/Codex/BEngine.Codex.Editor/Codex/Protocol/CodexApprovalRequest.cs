namespace BEngine.Editor.Codex;

public sealed class CodexApprovalRequest
{
    public string RequestIdJson { get; init; } = string.Empty;
    public CodexApprovalKind Kind { get; init; }
    public string ItemId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
}
