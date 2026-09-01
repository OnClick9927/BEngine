using BEngine.Serialization;

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
        var nextLayers = draft.SortingLayers.Select(static layer => new SortingLayerData
        {
            Value = layer.Value,
            Name = layer.Name.Trim(),
            BuiltIn = layer.BuiltIn,
            IsUi = layer.IsUi,
            BuiltInId = layer.BuiltInId
        }).ToList();
        var replacements = draft.TagReplacements
            .Select(static pair => new KeyValuePair<string, string>(pair.Key.Trim(), pair.Value.Trim()))
            .Where(static pair => !pair.Key.Equals(pair.Value, StringComparison.Ordinal))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        var layerReplacements = draft.LayerReplacements;
        var pendingAssets = PrepareAssetMigrations(replacements, layerReplacements);
        var openObjects = CaptureOpenObjectMigrations(replacements);
        var openLayers = CaptureOpenLayerMigrations(layerReplacements);

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
                foreach (var item in openObjects)
                    item.GameObject.tag = item.NextTag;
            }

            foreach (var pending in pendingAssets)
                pending.UpdatedText = YamlUtility.Serialize(pending.Data);

            foreach (var pending in pendingAssets)
            {
                WriteTextAtomic(pending.SourcePath, pending.UpdatedText);
                writtenAssets.Add(pending);
            }

            EditorProjectSettings.current.Tags = nextTags.ToList();
            EditorProjectSettings.current.SortingLayers = CloneLayers(nextLayers);
            EditorProjectSettings.Save();
            foreach (var item in openLayers) item.ApplyNext();
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
            foreach (var item in openLayers)
                TryRollback(item.ApplyPrevious, "restore an open Layer reference", rollbackErrors);
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

        if (replacements.Count > 0 || layerReplacements.Count > 0) Undo.ClearAll();

        foreach (var pending in pendingAssets)
        {
            try
            {
                AssetDatabase.ImportAsset(pending.AssetPath, ImportAssetOptions.ForceUpdate);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Tag/Layer references were saved, but '{pending.AssetPath}' could not be " +
                                 $"reimported immediately: {exception.Message}");
            }
        }

        EditorBridge.Host?.RepaintAllWindows();
    }

    internal static int RewriteAssetTags(object data,
        IReadOnlyDictionary<string, string> replacements)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(replacements);
        var gameObjects = data switch
        {
            SceneAssetData scene => scene.GameObjects,
            PrefabAssetData prefab => prefab.GameObjects,
            _ => throw new ArgumentException(
                $"Only {nameof(SceneAssetData)} and {nameof(PrefabAssetData)} contain GameObject Tags.",
                nameof(data))
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

    internal static int RewriteAssetLayers(object data,
        IReadOnlyDictionary<ulong, ulong> replacements)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(replacements);
        var gameObjects = data switch
        {
            SceneAssetData scene => scene.GameObjects,
            PrefabAssetData prefab => prefab.GameObjects,
            _ => throw new ArgumentException(
                $"Only {nameof(SceneAssetData)} and {nameof(PrefabAssetData)} contain Layer references.",
                nameof(data))
        };
        var changed = 0;
        foreach (var gameObject in gameObjects)
        {
            if (replacements.TryGetValue(gameObject.Layer, out var nextLayer) &&
                nextLayer != gameObject.Layer)
            {
                gameObject.Layer = nextLayer;
                changed++;
            }
            changed += RewriteFields(gameObject.Transform.Fields, replacements);
            foreach (var component in gameObject.Components)
                changed += RewriteFields(component.Fields, replacements);
        }
        return changed;
    }

    private static List<PendingAssetMigration> PrepareAssetMigrations(
        IReadOnlyDictionary<string, string> replacements,
        IReadOnlyDictionary<ulong, ulong> layerReplacements)
    {
        if (replacements.Count == 0 && layerReplacements.Count == 0 || EditorBridge.Host is null) return [];

        var result = new List<PendingAssetMigration>();
        Collect<SceneAssetData>("t:Scene", replacements, layerReplacements, result);
        Collect<PrefabAssetData>("t:Prefab", replacements, layerReplacements, result);
        return result;
    }

    private static void Collect<TData>(string filter,
        IReadOnlyDictionary<string, string> replacements,
        IReadOnlyDictionary<ulong, ulong> layerReplacements,
        ICollection<PendingAssetMigration> result)
        where TData : class
    {
        foreach (var guid in AssetDatabase.FindAssets(filter))
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(assetPath)) continue;
            var sourcePath = AssetDatabase.ResolveAssetPath(assetPath);
            try
            {
                var data = YamlUtility.Load<TData>(sourcePath);
                if (RewriteAssetTags(data, replacements) +
                    RewriteAssetLayers(data, layerReplacements) == 0) continue;
                result.Add(new PendingAssetMigration(assetPath, sourcePath,
                    File.ReadAllText(sourcePath), data));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Tag/Layer references in '{assetPath}' could not be prepared: {exception.Message}", exception);
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

    private static List<OpenLayerMigration> CaptureOpenLayerMigrations(
        IReadOnlyDictionary<ulong, ulong> replacements)
    {
        if (replacements.Count == 0 || EditorBridge.Host is not { } host) return [];
        var scenes = new List<Scene>();
        foreach (var scene in host.OpenScenes) AddSceneOnce(scenes, scene);
        if (host.CurrentPrefabStage is { } prefabStage) AddSceneOnce(scenes, prefabStage.scene);
        var result = new List<OpenLayerMigration>();
        foreach (var gameObject in scenes.SelectMany(static scene => scene.gameObjects))
        {
            if (replacements.TryGetValue(gameObject.layer, out var nextLayer))
                result.Add(new OpenLayerMigration(() => gameObject.layer,
                    value => gameObject.layer = value, nextLayer));

            foreach (var component in gameObject.components)
            {
                foreach (var member in ComponentFieldSerializer.GetSerializableMembers(component.GetType()))
                {
                    if (ComponentFieldSerializer.GetMemberType(member) != typeof(ulong)) continue;
                    if (ComponentFieldSerializer.GetMemberValue(member, component) is not ulong previous) continue;
                    ulong next;
                    if (IsLayerField(member.Name))
                        next = replacements.GetValueOrDefault(previous, previous);
                    else if (IsLayerMaskField(member.Name))
                        next = RemapMask(previous, replacements);
                    else
                        continue;
                    if (next == previous) continue;
                    result.Add(new OpenLayerMigration(
                        () => (ulong)(ComponentFieldSerializer.GetMemberValue(member, component) ?? 0UL),
                        value => ComponentFieldSerializer.SetMemberValue(member, component, value), next));
                }
            }
        }
        return result;
    }

    private static List<SortingLayerData> CloneLayers(
        IEnumerable<SortingLayerData> layers) => layers.Select(static layer => new SortingLayerData
    {
        Value = layer.Value,
        Name = layer.Name,
        BuiltIn = layer.BuiltIn,
        IsUi = layer.IsUi,
        BuiltInId = layer.BuiltInId
    }).ToList();

    private static int RewriteFields(IDictionary<string, string> fields,
        IReadOnlyDictionary<ulong, ulong> replacements)
    {
        var changed = 0;
        foreach (var key in fields.Keys.ToArray())
        {
            if (!ulong.TryParse(fields[key], out var value)) continue;
            ulong next;
            if (IsLayerField(key))
                next = replacements.GetValueOrDefault(value, value);
            else if (IsLayerMaskField(key))
                next = RemapMask(value, replacements);
            else
                continue;
            if (next == value) continue;
            fields[key] = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
            changed++;
        }
        return changed;
    }

    private static bool IsLayerField(string name) =>
        name.Equals("sortingLayer", StringComparison.OrdinalIgnoreCase);

    private static bool IsLayerMaskField(string name) =>
        name.Equals("cullingMask", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("LayerMask", StringComparison.OrdinalIgnoreCase);

    private static ulong RemapMask(ulong mask, IReadOnlyDictionary<ulong, ulong> replacements)
    {
        var sourceBits = 0UL;
        foreach (var oldLayer in replacements.Keys) sourceBits |= SortingLayer.ToMask(oldLayer);
        var result = mask & ~sourceBits;
        foreach (var (oldLayer, newLayer) in replacements)
        {
            var oldBit = SortingLayer.ToMask(oldLayer);
            if ((mask & oldBit) == 0) continue;
            result |= SortingLayer.ToMask(newLayer);
        }
        return result & SortingLayer.AllMask;
    }

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
        object data)
    {
        public string AssetPath { get; } = assetPath;
        public string SourcePath { get; } = sourcePath;
        public string PreviousText { get; } = previousText;
        public object Data { get; } = data;
        public string UpdatedText { get; set; } = string.Empty;
    }

    private sealed record OpenObjectMigration(
        GameObject GameObject,
        string PreviousTag,
        string NextTag);

    private sealed class OpenLayerMigration(
        Func<ulong> read,
        Action<ulong> write,
        ulong next)
    {
        private readonly ulong _previous = read();
        internal void ApplyNext() => write(next);
        internal void ApplyPrevious() => write(_previous);
    }
}
