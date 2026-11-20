namespace BEngine.ProjectSystem;

/// <summary>Optional host-specific configuration checks; the logic kernel validates only execution inputs.</summary>
public interface IRuntimeConfigurationExtension
{
    void Validate(RuntimeProjectData project);
    void Validate(RuntimeSettingsData settings);
}
