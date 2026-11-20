namespace BEngine.Editor;

public sealed class ProjectSettingsWindowController : SettingsWindowController
{
    public ProjectSettingsWindowController(
        EditorWindowMvcContext context,
        ProjectSettingsWindowModel model) : base(context, model)
    {
        Model = model;
        View = context.View as ProjectSettingsWindow ?? throw new ArgumentException(
            $"MVC context view is not {typeof(ProjectSettingsWindow).FullName}.", nameof(context));
    }

    public new ProjectSettingsWindowModel Model { get; }
    public new ProjectSettingsWindow View { get; }
}
