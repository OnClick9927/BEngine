namespace BEngine.Editor;

/// <summary>Unity-compatible settings page contract discovered from loaded editor assemblies.</summary>
public class SettingsProvider
{
    public SettingsProvider(string path, SettingsScope scopes, IEnumerable<string>? keywords = null)
    {
        settingsPath = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("Settings path cannot be empty.", nameof(path))
            : path.Replace('\\', '/').Trim('/');
        scope = scopes;
        this.keywords = keywords is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(keywords.Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.OrdinalIgnoreCase);
    }

    public string settingsPath { get; }
    public SettingsScope scope { get; }
    public string label { get; set; } = string.Empty;
    public HashSet<string> keywords { get; set; }
    public Action<string>? guiHandler { get; set; }
    public Action<string>? activateHandler { get; set; }
    public Action? deactivateHandler { get; set; }
    public Action? inspectorUpdateHandler { get; set; }
    public Action? footerBarGuiHandler { get; set; }
    public string sourceAssembly { get; internal set; } = string.Empty;
    public bool isPackageProvider => !sourceAssembly.Equals("BEngine.Editor", StringComparison.OrdinalIgnoreCase);
    public string displayName => string.IsNullOrWhiteSpace(label)
        ? settingsPath.Split('/').Last()
        : label;

    public virtual void OnActivate(string searchContext)
    {
        activateHandler?.Invoke(searchContext);
    }
    public virtual void OnGUI(string searchContext)
    {
        guiHandler?.Invoke(searchContext);
    }
    public virtual void OnDeactivate()
    {
        deactivateHandler?.Invoke();
    }
    public virtual void OnInspectorUpdate()
    {
        inspectorUpdateHandler?.Invoke();
    }
    public virtual void OnFooterBarGUI()
    {
        footerBarGuiHandler?.Invoke();
    }
}
