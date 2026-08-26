namespace BEngine.Editor;

internal static class EditorObjectPicker
{
    private const long PendingSelectionLifetimeMilliseconds = 60_000;
    private static readonly Dictionary<int, PendingSelection> PendingSelections = [];

    internal static object CaptureState() =>
        new PickerState(new Dictionary<int, PendingSelection>(PendingSelections));

    internal static void RestoreState(object state)
    {
        if (state is not PickerState snapshot) return;
        PendingSelections.Clear();
        foreach (var pair in snapshot.Selections) PendingSelections[pair.Key] = pair.Value;
    }

    internal static bool TryConsume(
        int token,
        Type objectType,
        bool allowSceneObjects,
        out BObject? value)
    {
        ValidateObjectType(objectType);
        PruneExpiredSelections();
        if (!PendingSelections.Remove(token, out var selection))
        {
            value = null;
            return false;
        }

        var pending = selection.Value;
        if (pending is null)
        {
            value = null;
            return true;
        }

        value = Coerce(pending, objectType, allowSceneObjects);
        return value is not null;
    }

    internal static void Open(
        int token,
        Rect anchor,
        BObject? current,
        Type objectType,
        bool allowSceneObjects)
    {
        ValidateObjectType(objectType);
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("None"), current is null, () => Select(token, null));

        var selected = SelectedObject(objectType, allowSceneObjects);
        if (selected is null)
        {
            menu.AddDisabledItem(new GUIContent("Use Selected"));
        }
        else
        {
            menu.AddItem(new GUIContent($"Use Selected: {DisplayName(selected)}"),
                SameObject(current, selected), () => Select(token, selected));
        }

        menu.AddSeparator(string.Empty);
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "None",
            "Use Selected"
        };
        foreach (var candidate in ProjectCandidates(objectType))
            AddCandidate(menu, usedPaths, token, current, candidate);
        if (allowSceneObjects)
            foreach (var candidate in SceneCandidates(objectType))
                AddCandidate(menu, usedPaths, token, current, candidate);

        menu.ShowAsAdvancedDropdown(anchor);
    }

    internal static bool TryHandleDrag(
        Rect position,
        Type objectType,
        bool allowSceneObjects,
        out BObject? value)
    {
        value = null;
        if (!GUI.enabled || Event.current.type is not (EventType.DragUpdated or EventType.DragPerform))
            return false;

        var rootPosition = GUI.GUIToRootPoint(new Vector2(position.x, position.y));
        var rootRect = new Rect(rootPosition.x, rootPosition.y, position.width, position.height);
        if (!rootRect.Contains(GUI.GUIToRootPoint(Event.current.mousePosition))) return false;

        var candidate = ResolveDraggedObject(objectType, allowSceneObjects);
        DragAndDrop.visualMode = candidate is null
            ? DragAndDropVisualMode.Rejected
            : DragAndDropVisualMode.Link;
        var perform = Event.current.type == EventType.DragPerform;
        Event.current.Use();
        if (!perform || candidate is null) return false;

        DragAndDrop.AcceptDrag();
        value = candidate;
        return true;
    }

    internal static BObject? ResolveDraggedObject(Type objectType, bool allowSceneObjects)
    {
        ValidateObjectType(objectType);
        foreach (var reference in DragAndDrop.objectReferences)
            if (Coerce(reference, objectType, allowSceneObjects) is { } candidate)
                return candidate;

        foreach (var path in DragAndDrop.paths)
        {
            var assetPath = NormalizeAssetPath(path);
            if (assetPath.Length == 0) continue;
            try
            {
                if (AssetDatabase.LoadAssetAtPath(assetPath, objectType) is { } asset &&
                    Coerce(asset, objectType, allowSceneObjects) is { } candidate)
                    return candidate;
            }
            catch (Exception exception)
            {
                EditorFeatureGuard.Report($"ObjectField.LoadDraggedAsset {assetPath}", exception);
            }
        }

        return null;
    }

    internal static GUIContent Content(BObject? value, Type objectType, bool mixed)
    {
        ValidateObjectType(objectType);
        if (mixed) return new GUIContent("-", tooltip: "Multiple different values");
        if (value is null)
            return new GUIContent($"None ({ObjectNames.NicifyVariableName(objectType.Name)})");

        var path = AssetDatabase.GetAssetPath(value);
        if (!objectType.IsInstanceOfType(value))
            return new GUIContent(
                $"{DisplayName(value)} (Type mismatch)",
                EditorBuiltinIcons.Toolbar.Error,
                $"{value.GetType().FullName} cannot be assigned to {objectType.FullName}.");
        var tooltip = path.Length > 0
            ? $"{path}\n{value.GetType().FullName}"
            : $"{SafeHierarchyPath(value)}\n{value.GetType().FullName}".Trim();
        return new GUIContent(DisplayName(value), IconPath(value, path), tooltip);
    }

    internal static void ValidateValue(BObject? value, Type objectType)
    {
        ValidateObjectType(objectType);
    }

    private static void AddCandidate(
        GenericMenu menu,
        HashSet<string> usedPaths,
        int token,
        BObject? current,
        ObjectCandidate candidate)
    {
        var path = UniquePath(candidate.MenuPath, candidate.Value, usedPaths);
        menu.AddItem(new GUIContent(path), SameObject(current, candidate.Value),
            () => Select(token, candidate.Value));
    }

    private static IEnumerable<ObjectCandidate> ProjectCandidates(Type objectType)
    {
        var host = EditorBridge.Host;
        if (host is null) yield break;
        foreach (var record in host.FindAssets(string.Empty)
                     .OrderBy(static record => record.AssetPath, StringComparer.OrdinalIgnoreCase))
        {
            BObject? asset;
            try
            {
                asset = AssetDatabase.LoadAssetAtPath(record.AssetPath, objectType);
            }
            catch (Exception exception)
            {
                EditorFeatureGuard.Report($"ObjectField.LoadAsset {record.AssetPath}", exception);
                continue;
            }

            if (asset is null || !objectType.IsInstanceOfType(asset)) continue;
            yield return new ObjectCandidate(asset, $"Project/{NormalizeMenuText(record.AssetPath)}");
        }
    }

    private static IEnumerable<ObjectCandidate> SceneCandidates(Type objectType)
    {
        var host = EditorBridge.Host;
        if (host is null) yield break;
        var scenes = host.CurrentPrefabStage is null
            ? host.OpenScenes.Append(host.ActiveScene)
            : [host.ActiveScene];
        foreach (var scene in scenes
                     .Where(static scene => scene.isCreated && scene.isLoaded)
                     .DistinctBy(static scene => scene.Id))
        {
            var sceneName = string.IsNullOrWhiteSpace(scene.name) ? "Untitled" : scene.name;
            foreach (var gameObject in scene.gameObjects)
            {
                var hierarchy = HierarchyPath(gameObject);
                if (objectType.IsInstanceOfType(gameObject))
                    yield return new ObjectCandidate(gameObject,
                        $"Scene/{NormalizeMenuText(sceneName)}/{NormalizeMenuText(hierarchy)} [GameObject]");

                for (var index = 0; index < gameObject.components.Count; index++)
                {
                    var component = gameObject.components[index];
                    if (!objectType.IsInstanceOfType(component)) continue;
                    var typeName = ObjectNames.NicifyVariableName(component.GetType().Name);
                    yield return new ObjectCandidate(component,
                        $"Scene/{NormalizeMenuText(sceneName)}/{NormalizeMenuText(hierarchy)}/{typeName} [{index}]");
                }
            }
        }
    }

    private static BObject? SelectedObject(Type objectType, bool allowSceneObjects)
    {
        if (Selection.activeObject is { } selected &&
            Coerce(selected, objectType, allowSceneObjects) is { } candidate)
            return candidate;
        return Selection.activeGameObject is { } gameObject
            ? Coerce(gameObject, objectType, allowSceneObjects)
            : null;
    }

    private static BObject? Coerce(BObject source, Type objectType, bool allowSceneObjects)
    {
        BObject? candidate = objectType.IsInstanceOfType(source) ? source : source switch
        {
            Component component when typeof(GameObject).IsAssignableFrom(objectType) =>
                ComponentOwner(component),
            GameObject gameObject when typeof(Component).IsAssignableFrom(objectType) =>
                gameObject.components.FirstOrDefault(objectType.IsInstanceOfType),
            _ => null
        };
        if (candidate is null && AssetDatabase.GetAssetPath(source) is { Length: > 0 } assetPath)
        {
            try
            {
                candidate = AssetDatabase.LoadAssetAtPath(assetPath, objectType);
            }
            catch (Exception exception)
            {
                EditorFeatureGuard.Report($"ObjectField.LoadAsset {assetPath}", exception);
            }
        }
        if (candidate is null || !objectType.IsInstanceOfType(candidate)) return null;
        if (candidate is GameObject or Component)
            return allowSceneObjects && IsLoadedSceneObject(candidate) ? candidate : null;
        return allowSceneObjects || AssetDatabase.Contains(candidate) ? candidate : null;
    }

    private static GameObject? ComponentOwner(Component component)
    {
        try { return component.gameObject; }
        catch (InvalidOperationException) { return null; }
    }

    private static bool IsLoadedSceneObject(BObject value)
    {
        Scene? scene;
        try
        {
            scene = value switch
            {
                GameObject gameObject => gameObject.scene,
                Component component => component.gameObject.scene,
                _ => null
            };
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (scene is not { isCreated: true, isLoaded: true }) return false;
        var host = EditorBridge.Host;
        if (host?.CurrentPrefabStage is not null) return ReferenceEquals(host.ActiveScene, scene);
        return host is null || ReferenceEquals(host.ActiveScene, scene) ||
               host.OpenScenes.Any(open => ReferenceEquals(open, scene));
    }

    private static string NormalizeAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var normalized = path.Replace('\\', '/').Trim();
        if (!Path.IsPathRooted(path) || EditorBridge.Host is not { } host) return normalized.TrimStart('/');
        try
        {
            var relative = Path.GetRelativePath(host.ProjectRootPath, Path.GetFullPath(path)).Replace('\\', '/');
            return relative == ".." || relative.StartsWith("../", StringComparison.Ordinal)
                ? string.Empty
                : relative;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    private static string IconPath(BObject value, string assetPath)
    {
        if (value is GameObject) return EditorBuiltinIcons.Components.GameObject;
        if (value is Component component) return EditorIconRegistry.GetComponentIconPath(component.GetType());
        if (assetPath.Length > 0) return EditorAssetIcons.GetIconPath(assetPath);
        return EditorIconRegistry.GetIconPath(value.GetType()) ?? EditorBuiltinIcons.Assets.Default;
    }

    private static string DisplayName(BObject value)
    {
        var name = string.IsNullOrWhiteSpace(value.name)
            ? ObjectNames.NicifyVariableName(value.GetType().Name)
            : value.name.Trim();
        return $"{name} ({ObjectNames.NicifyVariableName(value.GetType().Name)})";
    }

    private static string HierarchyPath(BObject value) => value switch
    {
        GameObject gameObject => HierarchyPath(gameObject),
        Component component => $"{HierarchyPath(component.gameObject)}/{component.GetType().Name}",
        _ => value.name
    };

    private static string SafeHierarchyPath(BObject value)
    {
        try { return HierarchyPath(value); }
        catch (InvalidOperationException) { return value.name; }
    }

    private static string HierarchyPath(GameObject gameObject)
    {
        var segments = new Stack<string>();
        for (var current = gameObject.transform; current is not null; current = current.parent)
            segments.Push(string.IsNullOrWhiteSpace(current.gameObject.name) ? "GameObject" : current.gameObject.name);
        return string.Join('/', segments);
    }

    private static string NormalizeMenuText(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string UniquePath(string path, BObject value, HashSet<string> usedPaths)
    {
        if (usedPaths.Add(path)) return path;
        var suffix = value.Id == Guid.Empty
            ? value.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture)
            : value.Id.ToString("N")[..8];
        var candidate = $"{path} [{suffix}]";
        var ordinal = 2;
        while (!usedPaths.Add(candidate)) candidate = $"{path} [{suffix}-{ordinal++}]";
        return candidate;
    }

    internal static bool SameObject(BObject? left, BObject? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.Id == Guid.Empty || left.Id != right.Id) return false;
        return left.GetType() == right.GetType() &&
               (left is GameObject or Component) == (right is GameObject or Component);
    }

    private static void Select(int token, BObject? value)
    {
        PruneExpiredSelections();
        PendingSelections[token] = new PendingSelection(
            value, Environment.TickCount64 + PendingSelectionLifetimeMilliseconds);
        EditorApplication.QueuePlayerLoopUpdate();
    }

    private static void PruneExpiredSelections()
    {
        if (PendingSelections.Count == 0) return;
        var now = Environment.TickCount64;
        foreach (var token in PendingSelections
                     .Where(pair => pair.Value.ExpiresAt < now)
                     .Select(static pair => pair.Key)
                     .ToArray())
            PendingSelections.Remove(token);
    }

    private static void ValidateObjectType(Type objectType)
    {
        ArgumentNullException.ThrowIfNull(objectType);
        if (!typeof(BObject).IsAssignableFrom(objectType))
            throw new ArgumentException($"{objectType.FullName} does not derive from {nameof(BObject)}.",
                nameof(objectType));
    }

    private readonly record struct ObjectCandidate(BObject Value, string MenuPath);
    private readonly record struct PendingSelection(BObject? Value, long ExpiresAt);
    private sealed record PickerState(IReadOnlyDictionary<int, PendingSelection> Selections);
}
