namespace BEngine.Launcher;

internal sealed class LauncherSettingsData
{
    public string Format { get; set; } = "BEngine.LauncherSettings";
    public int Version { get; set; } = 2;
    public string LastProjectDirectory { get; set; } = string.Empty;
    public List<LauncherProjectRecord> Projects { get; set; } = [];
}
