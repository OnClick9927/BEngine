namespace BEngine.Editor.Codex;

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
