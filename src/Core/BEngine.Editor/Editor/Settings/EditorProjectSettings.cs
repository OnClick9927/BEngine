using System.Globalization;
using BEngine.Rendering.Rhi;
using BEngine.Serialization;
using BEngine.Documents;
using BEngine.Editor.Documents;

namespace BEngine.Editor;

public static class EditorProjectSettings
{
    private static ProjectSettingsData _current = new();
    private static string _path = string.Empty;
    public static ProjectSettingsData current => _current;
    public static string settingsPath => _path;
    public static event Action? projectSettingsChanged;

    public static void Initialize(string path, string? fallbackProductName = null)
    {
        _path = Path.GetFullPath(path);
        try
        {
            _current = File.Exists(_path) ? YamlUtility.Load<ProjectSettingsData>(_path) :
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
        Validate(_current);
        _current.Save(_path);
        Apply();
        EditorCallbackDispatcher.Invoke(projectSettingsChanged, nameof(projectSettingsChanged));
    }

    private static void Apply()
    {
        NormalizeTagsAndLayers(_current);
        Application.companyName = _current.CompanyName;
        Application.productName = _current.ProductName;
        GraphicsBackendSettings.PreferredBackend = GraphicsBackendDefaults.Parse(_current.GraphicsBackend);
        TagManager.Configure(_current.Tags);
        SortingLayerRegistry.Configure(_current.SortingLayers.Select(item => item.ToDefinition()));
    }

    private static void Validate(ProjectSettingsData value)
    {
        if (value.Format != "BEngine.ProjectSettings" || value.Version != 3)
            throw new InvalidDataException($"Unsupported project settings '{value.Format}' v{value.Version}.");
        if (string.IsNullOrWhiteSpace(value.CompanyName) || string.IsNullOrWhiteSpace(value.ProductName))
            throw new InvalidDataException("Company and product names cannot be empty.");
        if (value.DefaultScreenWidth < 320 || value.DefaultScreenHeight < 200)
            throw new InvalidDataException("Default screen resolution is too small.");
        foreach (var symbol in value.ScriptingDefineSymbols)
        {
            if (string.IsNullOrWhiteSpace(symbol) ||
                !(char.IsLetter(symbol[0]) || symbol[0] == '_') ||
                symbol.Skip(1).Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
                throw new InvalidDataException($"Invalid scripting define symbol '{symbol}'.");
        }
        if (value.ScriptingDefineSymbols.Distinct(StringComparer.Ordinal).Count() !=
            value.ScriptingDefineSymbols.Count)
            throw new InvalidDataException("Scripting define symbols must be unique.");
        NormalizeTagsAndLayers(value);
        if (value.Tags.Distinct(StringComparer.Ordinal).Count() != value.Tags.Count)
            throw new InvalidDataException("Tags must be unique.");
        var namedLayers = value.SortingLayers.Select(static layer => layer.Name).ToArray();
        if (namedLayers.Any(string.IsNullOrWhiteSpace) ||
            namedLayers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != namedLayers.Length)
            throw new InvalidDataException("Sorting layer names must be non-empty and unique.");
    }

    private static void NormalizeTagsAndLayers(ProjectSettingsData value)
    {
        value.Tags ??= TagManager.CreateDefaultTags().ToList();
        value.SortingLayers ??= SortingLayerRegistry.CreateDefaults().Select(static layer => new SortingLayerData
        {
            Value = layer.Value,
            Name = layer.Name,
            BuiltIn = layer.IsBuiltIn,
            IsUi = layer.IsUi,
            BuiltInId = layer.BuiltInId
        }).ToList();
    }
}
