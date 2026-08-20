namespace BEngine.Editor;

internal sealed class EditorOpenScene(
    Scene scene,
    string sourcePath,
    string assetPath,
    bool loaded = true)
{
    public Scene Scene { get; set; } = scene;
    public string SourcePath { get; } = Path.GetFullPath(sourcePath);
    public string AssetPath { get; } = assetPath.Replace('\\', '/');
    public bool IsLoaded { get; set; } = loaded;
    public bool IsDirty { get; set; }

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Scene.name)) return Scene.name;
            var name = Path.GetFileName(SourcePath);
            return name.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase)
                ? name[..^".scene.yaml".Length]
                : Path.GetFileNameWithoutExtension(name);
        }
    }
}
