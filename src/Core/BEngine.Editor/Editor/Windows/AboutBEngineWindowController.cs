using System.Reflection;
using BEngine.Platform;

namespace BEngine.Editor;

public sealed class AboutBEngineWindowController :
    EditorWindowController<AboutBEngineWindow, AboutBEngineWindowModel>
{
    public AboutBEngineWindowController(
        EditorWindowMvcContext context,
        AboutBEngineWindowModel model) : base(context, model) { }

    public bool CanOpenDocumentation => Model.DocumentationPath is not null;

    public void RefreshInformation()
    {
        var platform = EditorServiceAccess.GetRequiredService<PlatformUtility>();
        var paths = EditorServiceAccess.GetRequiredService<PathUtility>();
        var assembly = typeof(AboutBEngineWindow).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        Model.EngineVersion = !string.IsNullOrWhiteSpace(informational)
            ? informational
            : assembly.GetName().Version?.ToString(3) ?? Application.version;
        Model.FrameworkDescription = platform.FrameworkDescription;
        Model.OperatingSystemDescription = platform.OperatingSystemDescription;
        Model.OperatingSystemArchitecture = platform.OperatingSystemArchitecture.ToString();
        Model.ProcessArchitecture = platform.ProcessArchitecture.ToString();
        Model.ProjectPath = string.IsNullOrWhiteSpace(EditorApplication.projectPath)
            ? "No project is open"
            : paths.GetFullPath(EditorApplication.projectPath);
        Model.DocumentationPath = PackageDocumentationCatalog.FindCoreDocumentation();
    }

    public string BuildSystemInfo()
    {
        RefreshInformation();
        var platform = EditorServiceAccess.GetRequiredService<PlatformUtility>();
        return string.Join(platform.NewLine,
        [
            $"BEngine {Model.EngineVersion}",
            $".NET: {Model.FrameworkDescription}",
            $"OS: {Model.OperatingSystemDescription}",
            $"OS Architecture: {Model.OperatingSystemArchitecture}",
            $"Process Architecture: {Model.ProcessArchitecture}",
            $"Project: {Model.ProjectPath}"
        ]);
    }

    public void CopySystemInfo() => GUIUtility.systemCopyBuffer = BuildSystemInfo();

    public void OpenDocumentation()
    {
        RefreshInformation();
        if (Model.DocumentationPath is { } path) Application.OpenURL(path);
    }
}
