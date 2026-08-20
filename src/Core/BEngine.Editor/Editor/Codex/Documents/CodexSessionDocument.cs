using BEngine.Documents;

namespace BEngine.Editor.Codex;

public sealed class CodexSessionDocument : Document
{
    public string Format { get; set; } = "BEngine.CodexSession";
    public int Version { get; set; } = 1;
    public string ThreadId { get; set; } = string.Empty;
    public List<CodexTranscriptEntry> Transcript { get; set; } = [];
}
