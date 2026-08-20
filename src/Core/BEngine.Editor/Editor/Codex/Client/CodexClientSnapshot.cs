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
    long Revision);
