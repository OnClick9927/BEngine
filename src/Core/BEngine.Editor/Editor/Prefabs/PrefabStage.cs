
namespace BEngine.Editor;

public sealed class PrefabStage
{
    internal PrefabStage(string assetPath, Scene scene, GameObject prefabContentsRoot)
    {
        this.assetPath = assetPath;
        this.scene = scene;
        this.prefabContentsRoot = prefabContentsRoot;
    }

    public string assetPath { get; }
    public Scene scene { get; }
    public GameObject prefabContentsRoot { get; }
    public bool isValid => !string.IsNullOrWhiteSpace(assetPath) && prefabContentsRoot is not null;
    public void SavePrefab() => EditorBridge.Host?.SavePrefabStage();
    public void CloseStage() => EditorBridge.Host?.ClosePrefabStage();
}
