namespace BEngine.Editor;

public sealed class PreferencesWindowController : SettingsWindowController
{
    public PreferencesWindowController(
        EditorWindowMvcContext context,
        PreferencesWindowModel model) : base(context, model)
    {
        Model = model;
        View = context.View as PreferencesWindow ?? throw new ArgumentException(
            $"MVC context view is not {typeof(PreferencesWindow).FullName}.", nameof(context));
    }

    public new PreferencesWindowModel Model { get; }
    public new PreferencesWindow View { get; }
}
