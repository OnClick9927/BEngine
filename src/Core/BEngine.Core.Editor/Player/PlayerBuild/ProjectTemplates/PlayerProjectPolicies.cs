using BEngine.ProjectSystem;
namespace BEngine.Editor.Build;
/// <summary>Optional project-specific policies composed by installed development frameworks.</summary>
public static class PlayerProjectPolicies
{
    public static event Action<ProjectWorkspace>? EnsureProject;
    /// <summary>
    /// Migrates framework-owned settings before the engine validates them. Contributors must
    /// unregister when unloaded and must not load or save build settings from this callback.
    /// </summary>
    public static event Action<ProjectWorkspace, PlayerBuildSettings>? NormalizeBuildSettings;
    public static void Ensure(ProjectWorkspace workspace) => EnsureProject?.Invoke(workspace);

    public static void NormalizeSettings(ProjectWorkspace workspace, PlayerBuildSettings settings)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(settings);
        NormalizeBuildSettings?.Invoke(workspace, settings);
    }
}
