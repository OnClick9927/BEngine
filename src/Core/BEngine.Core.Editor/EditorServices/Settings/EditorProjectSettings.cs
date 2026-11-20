using EditorCapabilities = BEngine.Editor.EditorServices;
using System.Globalization;
using BEngine.Rendering.Rhi;
using BEngine.Serialization;
using BEngine.Editor.Documents;
using BEngine.Platform;

namespace BEngine.Editor;

[RuntimeTypeAlias("BEngine.Editor.EditorProjectSettings, BEngine.EditorWorkbench.Editor")]
[RuntimeTypeAlias("BEngine.Editor.EditorProjectSettings, BEngine.EditorWorkbench.Editor, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null")]
public static class EditorProjectSettings
{
    private static ProjectSettingsData _current = new();
    private static string _path = string.Empty;
    private static FileUtility FileSystem => EditorCapabilities.GetRequiredService<FileUtility>();
    private static PathUtility Paths => EditorCapabilities.GetRequiredService<PathUtility>();
    public static ProjectSettingsData current => _current;
    public static string settingsPath => _path;
    public static event Action? projectSettingsChanged;

    public static void Initialize(string path, string? fallbackProductName = null)
    {
        _path = Paths.GetFullPath(path);
        try
        {
            _current = FileSystem.FileExists(_path)
                ? YamlUtility.Load<ProjectSettingsData>(_path, FileSystem, Paths)
                :
                new ProjectSettingsData { ProductName = fallbackProductName ?? "BEngine Game" };
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Project settings could not be loaded; defaults are active: {exception.Message}");
            _current = new ProjectSettingsData { ProductName = fallbackProductName ?? "BEngine Game" };
        }
        Apply();
    }

    public static void Save()
    {
        if (string.IsNullOrWhiteSpace(_path)) throw new InvalidOperationException("Project settings are not initialized.");
        NormalizeTagsAndLayers(_current);
        _current.Validate();
        FileSystem.WriteAllText(_path, YamlUtility.Serialize(_current));
        Apply();
        EditorCallbacks.Dispatch(projectSettingsChanged, nameof(projectSettingsChanged));
    }

    private static void Apply()
    {
        NormalizeTagsAndLayers(_current);
        RuntimeConfiguration.ApplyIdentity(_current.CompanyName, _current.ProductName);
        GraphicsBackendSettings.PreferredBackend = GraphicsBackendDefaults.Parse(_current.GraphicsBackend);
        TagManager.Configure(_current.Tags);
        LayerRegistry.Configure(_current.SortingLayers.Select(item => item.ToDefinition()));
    }

    private static void NormalizeTagsAndLayers(ProjectSettingsData value)
    {
        // Default identities and tag/layer definitions belong to the engine's
        // settings model, not to a second editor-owned copy.
        value.SortingLayers ??= new RuntimeSettingsData().SortingLayers;
    }
}
