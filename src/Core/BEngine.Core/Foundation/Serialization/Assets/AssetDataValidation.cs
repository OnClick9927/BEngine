using BEngine.ProjectSystem;

namespace BEngine.Serialization;

internal static class AssetDataValidation
{
    internal static void ValidateScene(SceneAssetData document)
    {
        if (document.Format != "BEngine.Scene")
            throw new InvalidDataException($"Unsupported document format '{document.Format}'.");
        if (document.Version != 2)
            throw new InvalidDataException($"Unsupported BEngine scene version {document.Version}.");
        ValidateGameObjects(document.GameObjects, "Scene");
    }

    internal static void ValidatePrefab(PrefabAssetData document)
    {
        PrefabDocumentValidation.Validate(document);
        ValidateGameObjects(document.GameObjects, "Prefab");
    }

    private static void ValidateGameObjects(IEnumerable<GameObjectData> gameObjects, string kind)
    {
        foreach (var gameObject in gameObjects)
        {
            if (!SortingLayer.IsValid(gameObject.Layer))
                throw new InvalidDataException(
                    $"{kind} GameObject '{gameObject.Name}' has invalid sorting layer {gameObject.Layer}.");
            if (string.IsNullOrWhiteSpace(gameObject.Tag)) continue;
            if (!TagManager.IsDefined(gameObject.Tag))
                throw new InvalidDataException(
                    $"{kind} GameObject '{gameObject.Name}' uses undefined tag '{gameObject.Tag}'.");
        }
    }

    internal static void ValidateOptionalRelativePath(string path, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (RuntimeServices.PathService.IsPathRooted(path) ||
            path.AsSpan().HasPathTraversal())
            throw new InvalidDataException($"{fieldName} must be a project-relative path.");
    }
}
