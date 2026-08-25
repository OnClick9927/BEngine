using BEngine.Documents;

namespace BEngine.Editor;

internal static class ProjectTagLayerSettingsApplier
{
    internal static void Apply(TagLayerSettingsDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!draft.IsDirty) return;
        if (!string.IsNullOrEmpty(draft.Error))
            throw new InvalidOperationException(draft.Error);

        EditorAssetWritePolicy.EnsureCanWrite("Changing project Tags and Layers");

        var nextTags = draft.Tags.Select(static tag => tag.Trim()).ToArray();
        var nextLayers = draft.SortingLayers.Select(static layer => new SortingLayerDocument
        {
            Value = layer.Value,
            Name = layer.Name.Trim()
        }).ToList();
        var replacements = draft.TagReplacements
            .Select(static pair => new KeyValuePair<string, string>(pair.Key.Trim(), pair.Value.Trim()))
            .Where(static pair => !pair.Key.Equals(pair.Value, StringComparison.Ordinal))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        var pendingAssets = PrepareAssetMigrations(replacements);
        var openObjects = CaptureOpenObjectMigrations(replacements);

        var previousTags = EditorProjectSettings.current.Tags.ToArray();
        var previousLayers = CloneLayers(EditorProjectSettings.current.SortingLayers);
        var settingsPath = EditorProjectSettings.settingsPath;
        var settingsExisted = File.Exists(settingsPath);
        var previousSettingsText = settingsExisted ? File.ReadAllText(settingsPath) : string.Empty;
        var writtenAssets = new List<PendingAssetMigration>();

        try
        {
            if (replacements.Count > 0)
            {
                TagManager.Configure(nextTags);
                foreach (var pending in pendingAssets)
                    pending.UpdatedText = pending.Document.ToYaml();
                foreach (var item in openObjects)
                    item.GameObject.tag = item.NextTag;
            }

            foreach (var pending in pendingAssets)
            {
                WriteTextAtomic(pending.SourcePath, pending.UpdatedText);
                writtenAssets.Add(pending);
            }

            EditorProjectSettings.current.Tags = nextTags.ToList();
            EditorProjectSettings.current.SortingLayers = CloneLayers(nextLayers);
            EditorProjectSettings.Save();
        }
        catch (Exception applyException)
        {
            var rollbackErrors = new List<Exception>();
            TryRollback(() => TagManager.Configure(previousTags),
                "restore the Tag registry", rollbackErrors);
            TryRollback(
                () => SortingLayerRegistry.Configure(previousLayers.Select(static item => item.ToDefinition())),
                "restore the Layer registry", rollbackErrors);
            foreach (var item in openObjects)
                TryRollback(() => item.GameObject.tag = item.PreviousTag,
                    $"restore Tag on GameObject '{item.GameObject.name}'", rollbackErrors);
            foreach (var pending in writtenAssets.AsEnumerable().Reverse())
                TryRollback(() => WriteTextAtomic(pending.SourcePath, pending.PreviousText),
                    $"restore '{pending.AssetPath}'", rollbackErrors);

            TryRollback(() => EditorProjectSettings.current.Tags = previousTags.ToList(),
                "restore Tags in memory", rollbackErrors);
            TryRollback(() => EditorProjectSettings.current.SortingLayers = CloneLayers(previousLayers),
                "restore Layers in memory", rollbackErrors);
            TryRollback(() => RestoreSettingsFile(settingsPath, settingsExisted, previousSettingsText),
                "restore ProjectSettings.yaml", rollbackErrors);
            if (rollbackErrors.Count > 0)
                throw new AggregateException(
                    "Applying Tags and Layers failed, and one or more rollback steps also failed.",
                    [applyException, .. rollbackErrors]);
            throw;
        }

        if (replacements.Count > 0) Undo.ClearAll();

        foreach (var pending in pendingAssets)
        {
            try
            {
                AssetDatabase.ImportAsset(pending.AssetPath, ImportAssetOptions.ForceUpdate);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Tag references were saved, but '{pending.AssetPath}' could not be " +
                                 $"reimported immediately: {exception.Message}");
            }
        }

        EditorBridge.Host?.RepaintAllWindows();
    }

    internal static int RewriteDocumentTags(Document document,
        IReadOnlyDictionary<string, string> replacements)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(replacements);
        var gameObjects = document switch
        {
            SceneDocument scene => scene.GameObjects,
            PrefabDocument prefab => prefab.GameObjects,
            _ => throw new ArgumentException(
                $"Only {nameof(SceneDocument)} and {nameof(PrefabDocument)} contain GameObject Tags.",
                nameof(document))
        };

        var changed = 0;
        foreach (var gameObject in gameObjects)
        {
            if (!replacements.TryGetValue(gameObject.Tag, out var replacement) ||
                replacement.Equals(gameObject.Tag, StringComparison.Ordinal)) continue;
            gameObject.Tag = replacement;
            changed++;
        }
        return changed;
    }

    private static List<PendingAssetMigration> PrepareAssetMigrations(
        IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0 || EditorBridge.Host is null) return [];

        var result = new List<PendingAssetMigration>();
        Collect<SceneDocument>("t:Scene", replacements, result);
        Collect<PrefabDocument>("t:Prefab", replacements, result);
        return result;
    }

    private static void Collect<TDocument>(string filter,
        IReadOnlyDictionary<string, string> replacements,
        ICollection<PendingAssetMigration> result)
        where TDocument : Document
    {
        foreach (var guid in AssetDatabase.FindAssets(filter))
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(assetPath)) continue;
            var sourcePath = AssetDatabase.ResolveAssetPath(assetPath);
            try
            {
                var document = Document.Load<TDocument>(sourcePath);
                if (RewriteDocumentTags(document, replacements) == 0) continue;
                result.Add(new PendingAssetMigration(assetPath, sourcePath,
                    File.ReadAllText(sourcePath), document));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Tag references in '{assetPath}' could not be prepared: {exception.Message}", exception);
            }
        }
    }

    private static List<OpenObjectMigration> CaptureOpenObjectMigrations(
        IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0 || EditorBridge.Host is not { } host) return [];
        var scenes = new List<Scene>();
        foreach (var scene in host.OpenScenes)
            AddSceneOnce(scenes, scene);
        if (host.CurrentPrefabStage is { } prefabStage)
            AddSceneOnce(scenes, prefabStage.scene);

        return scenes.SelectMany(static scene => scene.gameObjects)
            .Where(gameObject => replacements.ContainsKey(gameObject.tag))
            .Select(gameObject => new OpenObjectMigration(gameObject, gameObject.tag,
                replacements[gameObject.tag]))
            .ToList();
    }

    private static void AddSceneOnce(ICollection<Scene> scenes, Scene scene)
    {
        if (scenes.Any(candidate => ReferenceEquals(candidate, scene))) return;
        scenes.Add(scene);
    }

    private static List<SortingLayerDocument> CloneLayers(
        IEnumerable<SortingLayerDocument> layers) => layers.Select(static layer => new SortingLayerDocument
    {
        Value = layer.Value,
        Name = layer.Name
    }).ToList();

    private static void RestoreSettingsFile(string path, bool existed, string contents)
    {
        if (existed)
        {
            WriteTextAtomic(path, contents);
            return;
        }
        if (File.Exists(path)) File.Delete(path);
    }

    private static void TryRollback(Action action, string operation, ICollection<Exception> errors)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            errors.Add(new InvalidOperationException($"Could not {operation}: {exception.Message}", exception));
        }
    }

    private static void WriteTextAtomic(string path, string contents)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, contents);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private sealed class PendingAssetMigration(
        string assetPath,
        string sourcePath,
        string previousText,
        Document document)
    {
        public string AssetPath { get; } = assetPath;
        public string SourcePath { get; } = sourcePath;
        public string PreviousText { get; } = previousText;
        public Document Document { get; } = document;
        public string UpdatedText { get; set; } = string.Empty;
    }

    private sealed record OpenObjectMigration(
        GameObject GameObject,
        string PreviousTag,
        string NextTag);
}
