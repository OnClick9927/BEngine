namespace BEngine.Editor;

internal sealed record GenericMenuItem(
    string Path,
    bool On,
    bool Enabled,
    bool Separator,
    Action? Action);
