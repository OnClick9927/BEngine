using BEngine.Documents;

namespace BEngine.Serialization;

internal static class PrefabAssetSerialization
{
    internal static PrefabAsset Load(string path) => Document<PrefabAsset>
        .Read(path, sourcePath => CreateAsset(YamlUtility.Load<PrefabAssetData>(sourcePath), sourcePath))
        .ToAsset();

    internal static PrefabAsset Deserialize(string yaml, string sourcePath = "") => Document<PrefabAsset>
        .Parse(yaml, contents => CreateAsset(YamlUtility.Deserialize<PrefabAssetData>(contents), sourcePath))
        .ToAsset();

    internal static string Serialize(PrefabAsset prefab) => Document<PrefabAsset>.FromAsset(prefab)
        .Serialize(static asset => YamlUtility.Serialize(asset.Data));

    internal static string Serialize(GameObject root, Guid? assetId = null) =>
        YamlUtility.Serialize(Capture(root, assetId));

    internal static void Save(PrefabAsset prefab, string path) => Document<PrefabAsset>.FromAsset(prefab)
        .Write(path, static (asset, destination) => YamlUtility.Save(asset.Data, destination));

    internal static PrefabAsset Save(GameObject root, string path, Guid? assetId = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var data = Capture(root, assetId ?? TryReadAssetId(path));
        var prefab = CreateAsset(data, path);
        if (root.IsRuntimeOnly || root.SceneUnchecked is { } scene &&
            (scene.IsRuntimeOnly || SceneRuntime.IsRunningScene(scene)))
            prefab.IsRuntimeOnly = true;
        Save(prefab, path);
        return prefab;
    }

    internal static PrefabAssetData Capture(GameObject root, Guid? assetId = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var hierarchy = PrefabAssetOperations.Traverse(root).ToArray();
        var objectIds = hierarchy.ToDictionary(item => item.Id, item => item.PrefabSourceId ?? item.Id);
        var document = new PrefabAssetData
        {
            Id = assetId ?? root.PrefabAssetId ?? Guid.NewGuid(),
            Name = root.name,
            Root = objectIds[root.Id]
        };

        foreach (var gameObject in hierarchy)
        {
            var item = SceneAssetSerialization.FromGameObject(gameObject);
            item.Id = objectIds[gameObject.Id];
            item.Parent = gameObject.transform.parent is { } parent &&
                          objectIds.TryGetValue(parent.gameObject.Id, out var parentId) ? parentId : null;
            item.PrefabAsset = null;
            item.PrefabSource = null;
            item.Transform.Id = gameObject.transform.PrefabSourceId ?? gameObject.transform.Id;
            item.Transform.PrefabAsset = null;
            item.Transform.PrefabSource = null;
            foreach (var component in item.Components)
            {
                var live = gameObject.components.First(candidate => candidate.Id == component.Id);
                component.Id = live.PrefabSourceId ?? live.Id;
                component.PrefabAsset = null;
                component.PrefabSource = null;
            }
            document.GameObjects.Add(item);
        }

        return document;
    }

    internal static PrefabAsset CreateAsset(PrefabAssetData document, string sourcePath)
    {
        AssetDataValidation.ValidatePrefab(document);
        return new PrefabAsset(document, sourcePath) { Id = document.Id, name = document.Name };
    }

    private static Guid? TryReadAssetId(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { return YamlUtility.Load<PrefabAssetData>(path).Id; }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
                                          YamlDotNet.Core.YamlException) { return null; }
    }
}
