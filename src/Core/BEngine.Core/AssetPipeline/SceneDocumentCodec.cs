using BEngine.Serialization;

namespace BEngine;

internal sealed class SceneDocumentCodec : ISceneDocumentCodec
{
    private static Platform.FileUtility Files => RuntimeServices.GetRequiredService<Platform.FileUtility>();
    public Scene Load(string path, IServiceProvider? services = null) =>
        Document<Scene>.Read(path, source => SceneAssetDocuments.Parse(Files.ReadAllText(source), services)).ToAsset();
    public Scene Deserialize(string yaml, IServiceProvider? services = null) =>
        Document<Scene>.Parse(yaml, contents => SceneAssetDocuments.Parse(contents, services)).ToAsset();
    public string Serialize(Scene scene) => Document<Scene>.FromAsset(scene).Serialize(SceneAssetDocuments.Serialize);
    public void Save(Scene scene, string path) => Document<Scene>.FromAsset(scene)
        .Write(path, static (asset, destination) => DocumentFile.Write(destination, () => SceneAssetDocuments.Serialize(asset)));
}
