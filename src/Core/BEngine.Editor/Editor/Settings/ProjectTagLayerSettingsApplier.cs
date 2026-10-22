using BEngine.Serialization;
using BEngine.Platform;

namespace BEngine.Editor;

internal static class ProjectTagLayerSettingsApplier
{
    private static FileUtility FileSystem => EditorServiceAccess.GetRequiredService<FileUtility>();
    private static PathUtility Paths => EditorServiceAccess.GetRequiredService<PathUtility>();
    private static PlatformUtility Platform => EditorServiceAccess.GetRequiredService<PlatformUtility>();

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
        var openPrefabSnapshots = CapturePrefabSnapshotMigrations(replacements, layerReplacements);

        var previousTags = EditorProjectSettings.current.Tags.ToArray();
        var previousLayers = CloneLayers(EditorProjectSettings.current.SortingLayers);
        var settingsPath = EditorProjectSettings.settingsPath;
        var settingsExisted = FileSystem.FileExists(settingsPath);
        var previousSettingsText = settingsExisted ? FileSystem.ReadAllText(settingsPath) : string.Empty;
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
            foreach (var item in openPrefabSnapshots) item.Reference.Baseline = item.Next;
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
            foreach (var item in openPrefabSnapshots) item.Reference.Baseline = item.Previous;
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
            SceneAssetData scene => scene.GameObjects.Concat(scene.PrefabInstances.SelectMany(item => item.Baseline)),
            PrefabAssetData prefab => prefab.GameObjects.Concat(prefab.Instances.SelectMany(item => item.Baseline))
                .Concat(prefab.BasePrefab?.Baseline ?? []),
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
            SceneAssetData scene => scene.GameObjects.Concat(scene.PrefabInstances.SelectMany(item => item.Baseline)),
            PrefabAssetData prefab => prefab.GameObjects.Concat(prefab.Instances.SelectMany(item => item.Baseline))
                .Concat(prefab.BasePrefab?.Baseline ?? []),
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
            changed += RewriteSerializedLayerReferences(
                gameObject.Transform.Fields, gameObject.Transform.Graph, replacements);
            foreach (var component in gameObject.Components)
                changed += RewriteSerializedLayerReferences(
                    component.Fields, component.Graph, replacements);
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
                    FileSystem.ReadAllText(sourcePath), data));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Tag/Layer references in '{assetPath}' could not be prepared: {exception.Message}", exception);
            }
        }
    }

    private static List<PrefabSnapshotMigration> CapturePrefabSnapshotMigrations(
        IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<ulong, ulong> layers)
    {
        if (EditorBridge.Host is not { } host || tags.Count == 0 && layers.Count == 0) return [];
        var scenes = host.OpenScenes.ToList();
        if (host.CurrentPrefabStage is { } stage) AddSceneOnce(scenes, stage.scene);
        var result = new List<PrefabSnapshotMigration>();
        foreach (var reference in scenes.SelectMany(scene => scene.gameObjects)
                     .Select(item => item.PrefabInstance).OfType<PrefabReferenceData>().Distinct())
        {
            var snapshot = new SceneAssetData { GameObjects = PrefabMerge.Clone(reference.Baseline) };
            if (RewriteAssetTags(snapshot, tags) + RewriteAssetLayers(snapshot, layers) > 0)
                result.Add(new(reference, reference.Baseline, snapshot.GameObjects));
        }
        return result;
    }

    private sealed record PrefabSnapshotMigration(PrefabReferenceData Reference,
        List<GameObjectData> Previous, List<GameObjectData> Next);

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

    private static int RewriteSerializedLayerReferences(
        IDictionary<string, string> fields,
        ComponentGraphData? graph,
        IReadOnlyDictionary<ulong, ulong> replacements)
    {
        var changedMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        RewriteFields(fields, replacements, changedMembers);
        RewriteGraph(graph, replacements, changedMembers);
        return changedMembers.Count;
    }

    private static void RewriteFields(
        IDictionary<string, string> fields,
        IReadOnlyDictionary<ulong, ulong> replacements,
        ISet<string> changedMembers)
    {
        foreach (var key in fields.Keys.ToArray())
        {
            if (!ulong.TryParse(fields[key], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var value) ||
                !TryRemapLayerValue(key, value, replacements, out var next)) continue;
            fields[key] = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
            changedMembers.Add(key);
        }
    }

    private static void RewriteGraph(
        ComponentGraphData? graph,
        IReadOnlyDictionary<ulong, ulong> replacements,
        ISet<string> changedMembers)
    {
        if (graph is null) return;
        foreach (var member in graph.Members)
        {
            var valueData = member.Value;
            if (!valueData.Kind.Equals("Scalar", StringComparison.Ordinal) ||
                !IsSerializedUInt64(valueData.Type) ||
                !ulong.TryParse(valueData.Value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var value) ||
                !TryRemapLayerValue(member.Name, value, replacements, out var next)) continue;
            valueData.Value = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
            changedMembers.Add(member.Name);
        }
    }

    private static bool TryRemapLayerValue(
        string name,
        ulong value,
        IReadOnlyDictionary<ulong, ulong> replacements,
        out ulong next)
    {
        if (IsLayerField(name))
            next = replacements.GetValueOrDefault(value, value);
        else if (IsLayerMaskField(name))
            next = RemapMask(value, replacements);
        else
        {
            next = value;
            return false;
        }
        return next != value;
    }

    private static bool IsSerializedUInt64(string typeName)
    {
        var separator = typeName.IndexOf(',');
        var fullName = (separator >= 0 ? typeName[..separator] : typeName).Trim();
        return fullName.Equals(typeof(ulong).FullName, StringComparison.Ordinal);
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
        if (FileSystem.FileExists(path)) FileSystem.DeleteFile(path);
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
        var fullPath = Paths.GetFullPath(path);
        var directory = Paths.GetDirectoryName(fullPath) ?? Platform.CurrentDirectory;
        FileSystem.CreateDirectory(directory);
        var temporaryPath = Paths.Combine(directory,
            $".{Paths.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            FileSystem.WriteAllText(temporaryPath, contents);
            FileSystem.MoveFile(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (FileSystem.FileExists(temporaryPath)) FileSystem.DeleteFile(temporaryPath);
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
