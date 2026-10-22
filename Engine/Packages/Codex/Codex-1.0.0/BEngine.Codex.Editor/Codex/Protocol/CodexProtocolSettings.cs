namespace BEngine.Editor.Codex;

internal static class CodexProtocolSettings
{
    public const string DefaultApprovalPolicy = "untrusted";

    public static bool Normalize(CodexProjectSettingsData settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var approvalPolicy = NormalizeApprovalPolicy(settings.ApprovalPolicy);
        if (string.Equals(settings.ApprovalPolicy, approvalPolicy, StringComparison.Ordinal)) return false;
        settings.ApprovalPolicy = approvalPolicy;
        return true;
    }

    public static string NormalizeApprovalPolicy(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return DefaultApprovalPolicy;
        if (normalized.Equals("unlessTrusted", StringComparison.OrdinalIgnoreCase)) return "untrusted";
        if (normalized.Equals("onRequest", StringComparison.OrdinalIgnoreCase)) return "on-request";
        if (normalized.Equals("untrusted", StringComparison.OrdinalIgnoreCase)) return "untrusted";
        if (normalized.Equals("on-request", StringComparison.OrdinalIgnoreCase)) return "on-request";
        if (normalized.Equals("never", StringComparison.OrdinalIgnoreCase)) return "never";
        return DefaultApprovalPolicy;
    }
}
