namespace BEngine.Editor;

public class SettingsWindowController : EditorWindowController
{
    public SettingsWindowController(
        EditorWindowMvcContext context,
        SettingsWindowModel model) : base(context, model)
    {
        Model = model;
        View = context.View as SettingsWindowBase ?? throw new ArgumentException(
            $"MVC context view is not {typeof(SettingsWindowBase).FullName}.", nameof(context));
    }

    public new SettingsWindowModel Model { get; }
    public new SettingsWindowBase View { get; }

    public void ActivateWindow() => RefreshProviders(force: true);

    public void DeactivateWindow()
    {
        Deactivate(Model.Selected);
        Model.Selected = null;
        Model.ProviderItems = [];
    }

    public void SelectPath(string path)
    {
        Model.RequestedPath = path ?? string.Empty;
        RefreshProviders(force: true);
    }

    public void SetSearch(string search)
    {
        Model.Search = search ?? string.Empty;
        Repaint();
    }

    public void InspectorUpdate()
    {
        if (Model.RegistryVersion != SettingsProviderRegistry.version)
            RefreshProviders(force: true);
        if (Model.Selected is { } selected)
            EditorFeatureGuard.Invoke($"SettingsProvider {selected.settingsPath}.OnInspectorUpdate",
                selected.OnInspectorUpdate);
    }

    public void RefreshProviders(bool force)
    {
        if (!force && Model.RegistryVersion == SettingsProviderRegistry.version &&
            Model.ProviderItems.Length > 0) return;

        var previous = Model.Selected;
        var previousPath = previous?.settingsPath;
        Model.ProviderItems = SettingsProviderRegistry.GetProviders(View.settingsScope).ToArray();
        Model.RegistryVersion = SettingsProviderRegistry.version;
        var wanted = !string.IsNullOrWhiteSpace(Model.RequestedPath)
            ? Model.RequestedPath
            : previousPath;
        var next = Model.ProviderItems.FirstOrDefault(provider => provider.settingsPath.Equals(
                       wanted, StringComparison.OrdinalIgnoreCase)) ??
                   Model.ProviderItems.FirstOrDefault();
        Model.RequestedPath = string.Empty;
        if (ReferenceEquals(previous, next)) return;

        Deactivate(previous);
        Model.Selected = next;
        View.ResetProviderView();
        Activate(next);
    }

    public void Select(SettingsProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (ReferenceEquals(provider, Model.Selected)) return;
        Deactivate(Model.Selected);
        Model.Selected = provider;
        View.ResetProviderView();
        Activate(provider);
        Repaint();
    }

    public void LocaleChanged()
    {
        SettingsProviderRegistry.Invalidate();
        Repaint();
    }

    public void RegistryChanged()
    {
        Model.RegistryVersion = -1;
        Repaint();
    }

    private void Activate(SettingsProvider? provider)
    {
        Model.ActivationFailed = false;
        if (provider is null) return;
        Model.ActivationFailed = !EditorFeatureGuard.Invoke(
            $"SettingsProvider {provider.settingsPath}.OnActivate",
            () => provider.OnActivate(Model.Search));
    }

    private static void Deactivate(SettingsProvider? provider)
    {
        if (provider is not null)
            EditorFeatureGuard.Invoke($"SettingsProvider {provider.settingsPath}.OnDeactivate",
                provider.OnDeactivate);
    }
}
