using System.Text;
using System.Text.Json;

namespace BEngine.Editor.Codex;

public sealed record CodexProtocolEvent(
    CodexProtocolEventKind Kind,
    string Method = "",
    string ItemId = "",
    string TurnId = "",
    string Text = "",
    string Status = "",
    string AuthMode = "",
    string PlanType = "",
    CodexApprovalRequest? Approval = null);
