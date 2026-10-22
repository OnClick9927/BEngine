namespace BEngine.Editor.Codex;

public sealed record CodexThreadSummary(string Id, string Title, long UpdatedAt);
public sealed record CodexModelOption(string Id, string DisplayName, string DefaultEffort,
    IReadOnlyList<string> Efforts, bool IsDefault);
public sealed record CodexQuestionOption(string Label, string Description);
public sealed record CodexQuestion(string Id, string Header, string Text, bool IsSecret,
    IReadOnlyList<CodexQuestionOption> Options);
public sealed record CodexUserInputRequest(string RequestIdJson, IReadOnlyList<CodexQuestion> Questions);
