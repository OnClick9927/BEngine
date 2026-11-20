namespace BEngine.Editor;

public class SettingsWindowModel(string windowId) : EditorWindowModel(windowId)
{
    internal SettingsProvider[] ProviderItems { get; set; } = [];
    public IReadOnlyList<SettingsProvider> Providers => ProviderItems;
    public SettingsProvider? Selected { get; internal set; }
    public string RequestedPath { get; internal set; } = string.Empty;
    public string Search { get; internal set; } = string.Empty;
    public int RegistryVersion { get; internal set; } = -1;
    public bool ActivationFailed { get; internal set; }
}
