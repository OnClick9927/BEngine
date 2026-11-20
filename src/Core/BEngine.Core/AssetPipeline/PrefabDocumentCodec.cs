using BEngine.Serialization;

namespace BEngine;

internal sealed class PrefabDocumentCodec : IPrefabDocumentCodec
{
    private static Platform.FileUtility Files => RuntimeServices.GetRequiredService<Platform.FileUtility>();
    public PrefabAsset Load(string path) => Document<PrefabAsset>
        .Read(path, source => PrefabDocumentSnapshot.ParseAsset(Files.ReadAllText(source), source)).ToAsset();
    public PrefabAsset Deserialize(string yaml, string sourcePath = "") => Document<PrefabAsset>
        .Parse(yaml, contents => PrefabDocumentSnapshot.ParseAsset(contents, sourcePath)).ToAsset();
    public string Serialize(PrefabAsset prefab) => Document<PrefabAsset>.FromAsset(prefab).Serialize(PrefabDocumentSnapshot.SerializeAsset);
    public string Serialize(GameObject root, Guid? assetId = null) => PrefabDocumentSnapshot.Capture(root, assetId).ToYaml();
    public void Save(PrefabAsset prefab, string path) => Document<PrefabAsset>.FromAsset(prefab)
        .Write(path, static (asset, destination) => DocumentFile.Write(destination, () => PrefabDocumentSnapshot.SerializeAsset(asset)));

    public PrefabAsset Save(GameObject root, string path, Guid? assetId = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        PrefabDocumentSnapshot? existing = Files.FileExists(path)
            ? PrefabDocumentSnapshot.Parse(Files.ReadAllText(path)) : null;
        var data = PrefabDocumentSnapshot.Capture(root, assetId ?? existing?.Id ?? Guid.NewGuid());
        if (existing?.BaseReference is { } inherited)
        {
            var reference = inherited.Clone();
            var resolver = DependencyInjection.RuntimeServiceContributions.GetDefault<IPrefabResolutionBackend>() ??
                throw new InvalidOperationException("No source prefab resolver is registered.");
            var source = PrefabDocumentSnapshot.Resolve(resolver.Load(reference.Asset));
            reference.SetBaseline(PrefabDocumentSnapshot.Map(source, reference, connected: false).Objects);
            data.SetBase(reference);
        }
        var prefab = data.CreateAsset(path);
        Save(prefab, path);
        BAsset.Invalidate(path);
        return prefab;
    }

    public Guid? ReadAssetId(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Files.FileExists(path)) return null;
        try { return PrefabDocumentSnapshot.Parse(Files.ReadAllText(path)).Id; }
        catch (Exception exception) when (exception is IOException or InvalidDataException) { return null; }
    }
}
