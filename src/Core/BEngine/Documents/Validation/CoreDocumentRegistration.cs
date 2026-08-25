namespace BEngine.Documents;

using BEngine.AssetBundles;

internal static class CoreDocumentRegistration
{
    internal static void RegisterValidators()
    {
        DocumentValidationRegistry.Register<ProjectDocument>(ValidateProject);
        DocumentValidationRegistry.Register<ProjectSettingsDocument>(ValidateProjectSettings);
        DocumentValidationRegistry.Register<AssetBundleSettingsDocument>(ValidateAssetBundleSettings);
        DocumentValidationRegistry.Register<SceneDocument>(ValidateScene);
        DocumentValidationRegistry.Register<PrefabDocument>(ValidatePrefab);
    }

    internal static void ValidateProject(ProjectDocument document)
    {
        if (document.Format != "BEngine.Project" || document.Version != 1)
            throw new InvalidDataException($"Unsupported project document '{document.Format}' v{document.Version}.");
        if (document.Window.Width < 320 || document.Window.Height < 200)
            throw new InvalidDataException("Project window size is too small.");
        _ = Fix64.Parse(document.FixedDeltaTime);
    }

    internal static void ValidateScene(SceneDocument document)
    {
        if (document.Format != "BEngine.Scene")
            throw new InvalidDataException($"Unsupported document format '{document.Format}'.");
        if (document.Version != 1)
            throw new InvalidDataException($"Unsupported BEngine scene version {document.Version}.");
        ValidateGameObjects(document.GameObjects, "Scene");
    }

    internal static void ValidateProjectSettings(ProjectSettingsDocument document)
    {
        ProjectSettingsMigration.Normalize(document);
        if (document.Format != "BEngine.ProjectSettings" || document.Version is < 1 or > 2)
            throw new InvalidDataException(
                $"Unsupported project settings '{document.Format}' v{document.Version}.");
        if (string.IsNullOrWhiteSpace(document.CompanyName) || string.IsNullOrWhiteSpace(document.ProductName))
            throw new InvalidDataException("Company and product names cannot be empty.");
        if (document.DefaultScreenWidth < 320 || document.DefaultScreenHeight < 200)
            throw new InvalidDataException("Default screen resolution is too small.");
        if (document.Tags is null || document.Tags.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Project tags cannot contain empty values.");
        if (document.Tags.Select(static tag => tag.Trim()).Distinct(StringComparer.Ordinal).Count() !=
            document.Tags.Count)
            throw new InvalidDataException("Project tags must be unique.");
        if (document.SortingLayers is null || document.SortingLayers.Count != SortingLayer.MaximumIndex)
            throw new InvalidDataException("Project settings must define all 63 sorting layers.");
        foreach (var layer in document.SortingLayers) SortingLayer.Validate(layer.Value);
        if (document.SortingLayers.Select(item => item.Value).Distinct().Count() != SortingLayer.MaximumIndex)
            throw new InvalidDataException("Project sorting layer values must be unique.");
        if (document.SortingLayers.Select(item => item.Name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != SortingLayer.MaximumIndex)
            throw new InvalidDataException("Project sorting layer names must be non-empty and unique.");
        if (document.ScriptingDefineSymbols is null ||
            document.ScriptingDefineSymbols.Any(static symbol => !IsValidSymbol(symbol)) ||
            document.ScriptingDefineSymbols.Distinct(StringComparer.Ordinal).Count() !=
            document.ScriptingDefineSymbols.Count)
            throw new InvalidDataException("Scripting define symbols must be valid and unique.");
    }

    internal static void ValidateAssetBundleSettings(AssetBundleSettingsDocument document)
    {
        if (document.Format != "BEngine.AssetBundleSettings" || document.Version != 1)
            throw new InvalidDataException(
                $"Unsupported AssetBundle settings '{document.Format}' v{document.Version}.");
        if (string.IsNullOrWhiteSpace(document.PackageName) || document.PackageName.Length > 128 ||
            document.PackageName[0] is not (>= 'a' and <= 'z') and
                not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') ||
            document.PackageName.Any(character => character is not (>= 'a' and <= 'z') and
                not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '.' and not '_' and not '-'))
            throw new InvalidDataException("AssetBundle package name is invalid.");
        ValidateOptionalRelativePath(document.BuiltInDirectory, nameof(document.BuiltInDirectory));
        ValidateOptionalRelativePath(document.CacheDirectory, nameof(document.CacheDirectory));
        if (!string.IsNullOrWhiteSpace(document.RemoteBaseUrl) &&
            (!Uri.TryCreate(document.RemoteBaseUrl, UriKind.Absolute, out var remote) ||
             remote.Scheme is not ("http" or "https")))
            throw new InvalidDataException("AssetBundle remote base URL must be an absolute HTTP(S) URL.");
        if (document.RequireHttps && !string.IsNullOrWhiteSpace(document.RemoteBaseUrl) &&
            !document.RemoteBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("AssetBundle remote base URL must use HTTPS.");
        if (document.MaxRetries < 0)
            throw new InvalidDataException("AssetBundle retry value is invalid.");
    }

    internal static void ValidatePrefab(PrefabDocument document)
    {
        if (document.Format != "BEngine.Prefab")
            throw new InvalidDataException($"Unsupported document format '{document.Format}'.");
        if (document.Version != 1)
            throw new InvalidDataException($"Unsupported BEngine prefab version {document.Version}.");
        if (document.GameObjects.Count == 0 || document.GameObjects.All(item => item.Id != document.Root))
            throw new InvalidDataException("Prefab document has no valid root GameObject.");
        var ids = document.GameObjects.Select(item => item.Id).ToHashSet();
        if (ids.Count != document.GameObjects.Count)
            throw new InvalidDataException("Prefab document contains duplicate GameObject IDs.");
        if (document.GameObjects.Any(item => item.Parent.HasValue && !ids.Contains(item.Parent.Value)))
            throw new InvalidDataException("Prefab document contains a parent outside its hierarchy.");
        ValidateGameObjects(document.GameObjects, "Prefab");
    }

    private static void ValidateGameObjects(IEnumerable<GameObjectDocument> gameObjects, string kind)
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

    private static bool IsValidSymbol(string symbol) => !string.IsNullOrWhiteSpace(symbol) &&
        (char.IsLetter(symbol[0]) || symbol[0] == '_') &&
        symbol.Skip(1).All(static character => char.IsLetterOrDigit(character) || character == '_');

    private static void ValidateOptionalRelativePath(string path, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (Path.IsPathRooted(path) || path.Replace('\\', '/').Split('/').Any(segment => segment is ".." or "."))
            throw new InvalidDataException($"{fieldName} must be a project-relative path.");
    }
}
