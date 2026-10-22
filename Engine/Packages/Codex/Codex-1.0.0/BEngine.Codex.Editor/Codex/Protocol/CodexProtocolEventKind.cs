using System.Text;
using System.Text.Json;

namespace BEngine.Editor.Codex;

public enum CodexProtocolEventKind
{
    Unknown,
    AgentDelta,
    AgentCompleted,
    ActivityStarted,
    ActivityCompleted,
    PlanUpdated,
    TurnStarted,
    TurnCompleted,
    Error,
    Warning,
    AccountUpdated,
    LoginCompleted,
    ApprovalRequested
}
