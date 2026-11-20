using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Serialization;
using BEngine.Platform;

namespace BEngine.Editor;

internal static class ProjectTagLayerSettingsApplier
{
    private static FileUtility FileSystem => EditorCapabilities.GetRequiredService<FileUtility>();
    private static PathUtility Paths => EditorCapabilities.GetRequiredService<PathUtility>();
    private static PlatformUtility Platform => EditorCapabilities.GetRequiredService<PlatformUtility>();

    internal static void Apply(TagLayerSettingsDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!draft.IsDirty) return;
        if (!string.IsNullOrEmpty(draft.Error))
            throw new InvalidOperationException(draft.Error);

        EditorAssetWritePolicy.EnsureCanWrite("Changing project Tags and Layers");

        var nextTags = Gather828(draft.Tags);
        [global::BEngine.SkipLoopGuard]
        string[] Gather828(global::System.Collections.Generic.IEnumerable<string> items828)
        {
            ArgumentNullException.ThrowIfNull(items828, "source");
            string Evaluate828(string tag) => tag.Trim();
            global::System.ReadOnlySpan<string> values;
            if (items828.GetType() == typeof(string[]))
                values = (string[])items828;
            else if (items828.GetType() == typeof(global::System.Collections.Generic.List<string>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)items828);
            else
            {
                return global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(items828, Evaluate828));
            }

            if (values.IsEmpty)
                return global::System.Array.Empty<string>();
            var result = new string[values.Length];
            for (var index = 0; index < values.Length; index++)
                result[index] = Evaluate828(values[index]);
            return result;
        }
        var nextLayers = Gather909(draft.SortingLayers);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.List<global::BEngine.ProjectSystem.SortingLayerData> Gather909(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.SortingLayerData> items909)
        {
            ArgumentNullException.ThrowIfNull(items909, "source");
            global::BEngine.ProjectSystem.SortingLayerData Evaluate909(global::BEngine.ProjectSystem.SortingLayerData layer) => new SortingLayerData
            {
                Value = layer.Value,
                Name = layer.Name.Trim(),
                BuiltIn = layer.BuiltIn,
                IsUi = layer.IsUi,
                BuiltInId = layer.BuiltInId
            };
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.SortingLayerData> values;
            if (items909.GetType() == typeof(global::BEngine.ProjectSystem.SortingLayerData[]))
                values = (global::BEngine.ProjectSystem.SortingLayerData[])items909;
            else if (items909.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.ProjectSystem.SortingLayerData>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.ProjectSystem.SortingLayerData>)items909);
            else
            {
                return global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Select(items909, Evaluate909));
            }

            var result = new global::System.Collections.Generic.List<global::BEngine.ProjectSystem.SortingLayerData>(values.Length);
            global::System.Runtime.InteropServices.CollectionsMarshal.SetCount(result, values.Length);
            var destination = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(result);
            for (var index = 0; index < values.Length; index++)
                destination[index] = Evaluate909(values[index]);
            return result;
        }
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
                pending.UpdatedText = pending.Data.ToYaml();

            foreach (var pending in pendingAssets)
            {
                WriteTextAtomic(pending.SourcePath, pending.UpdatedText);
                writtenAssets.Add(pending);
            }

            EditorProjectSettings.current.Tags = nextTags.ToList();
            EditorProjectSettings.current.SortingLayers = CloneLayers(nextLayers);
            EditorProjectSettings.Save();
            foreach (var item in openLayers) item.ApplyNext();
            foreach (var item in openPrefabSnapshots) item.Apply();
        }
        catch (Exception applyException)
        {
            var rollbackErrors = new List<Exception>();
            TryRollback(() => TagManager.Configure(previousTags),
                "restore the Tag registry", rollbackErrors);
            TryRollback(
                () => LayerRegistry.Configure(previousLayers.Select(static item => item.ToDefinition())),
                "restore the Layer registry", rollbackErrors);
            foreach (var item in openObjects)
                TryRollback(() => item.GameObject.tag = item.PreviousTag,
                    $"restore Tag on GameObject '{item.GameObject.name}'", rollbackErrors);
            foreach (var item in openPrefabSnapshots)
                TryRollback(item.Rollback, "restore a Prefab baseline", rollbackErrors);
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

    private static IEnumerable<SerializedHierarchy> Hierarchies(DocumentEdit document)
    {
        yield return document.Objects;
        foreach (var reference in document.References) yield return reference.Baseline;
        if (document.BaseReference is { } inherited) yield return inherited.Baseline;
    }

    private static int RewriteAssetTags(DocumentEdit document, IReadOnlyDictionary<string, string> replacements)
    {
        var changed = 0;
        foreach (var hierarchy in Hierarchies(document))
            changed += hierarchy.RewriteMetadata(tag => replacements.GetValueOrDefault(tag, tag));
        return changed;
    }

    private static int RewriteAssetLayers(DocumentEdit document, IReadOnlyDictionary<ulong, ulong> replacements)
    {
        var changed = 0;
        foreach (var hierarchy in Hierarchies(document)) changed += RewriteHierarchyLayers(hierarchy, replacements);
        return changed;
    }

    private static int RewriteHierarchyLayers(SerializedHierarchy hierarchy, IReadOnlyDictionary<ulong, ulong> replacements) =>
        hierarchy.RewriteMetadata(layers: value => replacements.GetValueOrDefault(value, value)) +
        hierarchy.RewriteScalars((name, type, text) => RewriteLayerScalar(name, type, text, replacements));

    private static string RewriteLayerScalar(string name, string? type, string text,
        IReadOnlyDictionary<ulong, ulong> replacements) =>
        (type is null || IsSerializedUInt64(type)) &&
        ulong.TryParse(text, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var value) &&
        TryRemapLayerValue(name, value, replacements, out var next)
            ? next.ToString(System.Globalization.CultureInfo.InvariantCulture) : text;

    private sealed record DocumentEdit(SerializedHierarchy Objects, PrefabReferenceSet References,
        PrefabReferenceSnapshot? BaseReference, Func<string> ToYaml);

    private static List<PendingAssetMigration> PrepareAssetMigrations(
        IReadOnlyDictionary<string, string> replacements,
        IReadOnlyDictionary<ulong, ulong> layerReplacements)
    {
        if (replacements.Count == 0 && layerReplacements.Count == 0 || EditorBridge.Host is null) return [];
        var result = new List<PendingAssetMigration>();
        Collect("t:Scene", text =>
        {
            var document = SceneDocumentSnapshot.Parse(text);
            return new(document.Objects, document.References, null, document.ToYaml);
        }, replacements, layerReplacements, result);
        Collect("t:Prefab", text =>
        {
            var document = PrefabDocumentSnapshot.Parse(text);
            return new(document.Objects, document.References, document.BaseReference, document.ToYaml);
        }, replacements, layerReplacements, result);
        return result;
    }

    private static void Collect(string filter, Func<string, DocumentEdit> parse,
        IReadOnlyDictionary<string, string> replacements,
        IReadOnlyDictionary<ulong, ulong> layerReplacements,
        ICollection<PendingAssetMigration> result)
    {
        foreach (var guid in AssetDatabase.FindAssets(filter))
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(assetPath)) continue;
            var sourcePath = AssetDatabase.ResolveAssetPath(assetPath);
            try
            {
                var previousText = FileSystem.ReadAllText(sourcePath);
                var data = parse(previousText);
                if (RewriteAssetTags(data, replacements) + RewriteAssetLayers(data, layerReplacements) == 0) continue;
                result.Add(new PendingAssetMigration(assetPath, sourcePath, previousText, data));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Tag/Layer references in '{assetPath}' could not be prepared: {exception.Message}", exception);
            }
        }
    }

    private static IReadOnlyList<PrefabBaselineChange> CapturePrefabSnapshotMigrations(
        IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<ulong, ulong> layers)
    {
        if (EditorBridge.Host is not { } host || tags.Count == 0 && layers.Count == 0) return [];
        var scenes = host.OpenScenes.ToList();
        if (host.CurrentPrefabStage is { } stage) AddSceneOnce(scenes, stage.scene);
        return PrefabBaselineChange.Prepare(scenes.SelectMany(scene => scene.gameObjects), hierarchy =>
            hierarchy.RewriteMetadata(tag => tags.GetValueOrDefault(tag, tag)) + RewriteHierarchyLayers(hierarchy, layers) > 0);
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
        bool __queryResult12328;
        {
            var __querySource12328 = scenes;
            ArgumentNullException.ThrowIfNull(__querySource12328, "source");
            __queryResult12328 = false;
            var __querySource12328Type = __querySource12328.GetType();
            if (__querySource12328Type == typeof(BEngine.Scene[]) || __querySource12328Type == typeof(System.Collections.Generic.List<BEngine.Scene>))
            {
                System.ReadOnlySpan<BEngine.Scene> __querySource12328Span = __querySource12328Type == typeof(BEngine.Scene[]) ? (BEngine.Scene[])(object)__querySource12328 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<BEngine.Scene>)(object)__querySource12328);
                foreach (var __queryItem12328 in __querySource12328Span)
                {
                    if ((ReferenceEquals(__queryItem12328, scene)))
                    {
                        __queryResult12328 = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var __queryItem12328 in __querySource12328)
                {
                    if ((ReferenceEquals(__queryItem12328, scene)))
                    {
                        __queryResult12328 = true;
                        break;
                    }
                }
            }
        }
        
        if (__queryResult12328)
            return;
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
        DocumentEdit data)
    {
        public string AssetPath { get; } = assetPath;
        public string SourcePath { get; } = sourcePath;
        public string PreviousText { get; } = previousText;
        public DocumentEdit Data { get; } = data;
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
