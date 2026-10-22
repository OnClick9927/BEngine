namespace BEngine.Editor;

public sealed record GenericMenuItem(
    string Path,
    bool On,
    bool Enabled,
    bool Separator,
    Action? Action);
