namespace BEngine.Launcher;

internal sealed record EditorStartupOutcome(
    EditorStartupOutcomeKind Kind,
    string Message,
    string? LogPath = null,
    int? ExitCode = null)
{
    internal bool IsReady => Kind == EditorStartupOutcomeKind.Ready;
}
