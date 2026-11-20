namespace BEngine.ProjectSystem;

public sealed class ProjectSettingsData : RuntimeSettingsData
{
    public string Format { get; set; } = "BEngine.ProjectSettings";
    public int Version { get; set; } = 3;
    public string Locale { get; set; } = "zh-CN";
    public List<string> ScriptingDefineSymbols { get; set; } = [];

    /// <summary>Checks the authoring format and engine/extension invariants without writing or applying settings.</summary>
    public void Validate() => BEngine.Serialization.ProjectDocumentValidation.ValidateProjectSettings(this);

    public RuntimeSettingsData ToRuntime() => new()
    {
        CompanyName = CompanyName, ProductName = ProductName, DefaultScreenWidth = DefaultScreenWidth,
        DefaultScreenHeight = DefaultScreenHeight, FullScreen = FullScreen, RunInBackground = RunInBackground,
        GraphicsBackend = GraphicsBackend, Tags = new(Tags), SortingLayers = SortingLayers.Select(static layer =>
            new SortingLayerData { Value = layer.Value, Name = layer.Name, BuiltIn = layer.BuiltIn,
                IsUi = layer.IsUi, BuiltInId = layer.BuiltInId }).ToList()
    };
}
