using BEngine.Documents;

namespace BEngine.Editor.Codex;

public sealed class CodexProjectSettingsData
{
    public string Format { get; set; } = "BEngine.CodexSettings";
    public int Version { get; set; } = 1;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Effort { get; set; } = "medium";
    public string ApprovalPolicy { get; set; } = CodexProtocolSettings.DefaultApprovalPolicy;
    public bool NetworkAccess { get; set; } = true;
}
