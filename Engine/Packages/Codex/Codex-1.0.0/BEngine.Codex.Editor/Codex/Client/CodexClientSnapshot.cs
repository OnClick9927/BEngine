namespace BEngine.Editor.Codex;

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
    long Revision)
{
    public IReadOnlyList<CodexThreadSummary> Threads { get; init; } = [];
    public IReadOnlyList<CodexModelOption> Models { get; init; } = [];
    public IReadOnlyList<CodexUserInputRequest> Questions { get; init; } = [];
    public string NextThreadCursor { get; init; } = string.Empty;
    public string ThreadTitle { get; init; } = string.Empty;
    public string CatalogError { get; init; } = string.Empty;
    public bool ShowingArchived { get; init; }
}
