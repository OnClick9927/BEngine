namespace BEngine.ProjectSystem;

public sealed class ProjectSettingsData
{
    private List<string> _tags = TagManager.CreateDefaultTags().ToList();

    public string Format { get; set; } = "BEngine.ProjectSettings";
    public int Version { get; set; } = 3;
    public string Locale { get; set; } = "zh-CN";
    public string CompanyName { get; set; } = "DefaultCompany";
    public string ProductName { get; set; } = "BEngine Game";
    public int DefaultScreenWidth { get; set; } = 1280;
    public int DefaultScreenHeight { get; set; } = 720;
    public bool FullScreen { get; set; }
    public string GraphicsBackend { get; set; } = "Vulkan";
    public List<string> ScriptingDefineSymbols { get; set; } = [];
    public List<string> Tags
    {
        get => _tags;
        set => _tags = value is { Count: > 0 } ? value : TagManager.CreateDefaultTags().ToList();
    }
    public List<SortingLayerData> SortingLayers { get; set; } = CreateDefaultSortingLayers();

    private static List<SortingLayerData> CreateDefaultSortingLayers() =>
        SortingLayerRegistry.CreateDefaults().Select(item => new SortingLayerData
        {
            Value = item.Value,
            Name = item.Name,
            BuiltIn = item.IsBuiltIn,
            IsUi = item.IsUi,
            BuiltInId = item.BuiltInId
        }).ToList();
}
