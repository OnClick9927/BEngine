using BEngine.Documents;

namespace BEngine.Editor.Documents;

public sealed class LauncherSettingsDocument : Document
{
    public string Format { get; set; } = "BEngine.LauncherSettings";
    public int Version { get; set; } = 1;
    public string LastProjectDirectory { get; set; } = string.Empty;
}
