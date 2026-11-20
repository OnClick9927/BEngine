using System.Collections;
using BEngine.Serialization;

namespace BEngine.Editor;

public enum PrefabOverrideKind { Property, AddedComponent, RemovedComponent, AddedGameObject, RemovedGameObject }

public sealed record PrefabOverride(GameObject InstanceRoot, BObject Target, PrefabOverrideKind Kind,
    string PropertyPath, Guid SourceId);

public static partial class PrefabUtility
{
    public static PrefabAsset SaveAsPrefabVariant(GameObject instanceRoot, string assetPath, bool connect = true)
    {
        EnsurePrefabEditing(instanceRoot);
        var sourcePath = GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new InvalidOperationException("A variant requires a prefab source.");
        if (ResolveAssetPath(sourcePath).FullPath.Equals(ResolveAssetPath(assetPath).FullPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Save the variant to a different asset path.");
        var source = LoadPrefabAsset(sourcePath);
        return SaveAsset(instanceRoot, assetPath, connect, out _, source)!;
    }

    public static IReadOnlyList<PrefabOverride> GetOverrides(GameObject instanceRoot, bool includeDefaultOverrides = false)
    {
        using var context = new OverrideContext(instanceRoot);
        var result = new List<PrefabOverride>();
        var liveObjects = PrefabAssetOperations.Traverse(context.Root).ToArray();
        foreach (var gameObject in liveObjects)
        {
            if (!context.TrySource(gameObject, out var sourceObject))
            {
                if (gameObject.transform.parent is null || context.TrySource(gameObject.transform.parent.gameObject, out _))
                    result.Add(new(context.Root, gameObject, PrefabOverrideKind.AddedGameObject, "", Guid.Empty));
                continue;
            }
            Compare(gameObject, sourceObject);
            foreach (var component in gameObject.components)
            {
                if (!context.TrySource(component, out var sourceComponent))
                    result.Add(new(context.Root, component, PrefabOverrideKind.AddedComponent, "", Guid.Empty));
                else Compare(component, sourceComponent);
            }
        }
        foreach (var sourceObject in PrefabAssetOperations.Traverse(context.Contents))
        {
            if (!context.TryLive(sourceObject, out var liveObject))
            {
                if (sourceObject.transform.parent is { } parent && context.TryLive(parent.gameObject, out _))
                    result.Add(new(context.Root, context.Root, PrefabOverrideKind.RemovedGameObject, sourceObject.name, sourceObject.Id));
                continue;
            }
            foreach (var component in sourceObject.components)
                if (component is not Transform && !context.TryLive(component, out _))
                    result.Add(new(context.Root, liveObject, PrefabOverrideKind.RemovedComponent, component.GetType().Name, component.Id));
        }
        return result;

        void Compare(BObject live, BObject source)
        {
            using var liveSerialized = new SerializedObject(live);
            using var sourceSerialized = new SerializedObject(source);
            foreach (var path in PropertyPaths(live, liveSerialized))
            {
                if (!includeDefaultOverrides && ReferenceEquals(live, context.Root.transform) && path is "localPosition" or "localRotation") continue;
                var property = liveSerialized.FindProperty(path);
                var inherited = sourceSerialized.FindProperty(path);
                if (property is not null && inherited is not null) CompareProperty(property, inherited, 0);
            }
            void CompareProperty(SerializedProperty property, SerializedProperty inherited, int depth)
            {
                if (!property.editable || ValuesEqual(property.boxedValue, inherited.boxedValue, context)) return;
                var children = depth < 16 && !property.isArray
                    ? liveSerialized.GetTraversalChildren(property, visibleOnly: false) : [];
                if (children.Count > 0 && Query4243())
                {
                    foreach (var child in children)
                        CompareProperty(child, sourceSerialized.FindProperty(child.propertyPath)!, depth + 1);
                }
                else
                    result.Add(new(context.Root, live, PrefabOverrideKind.Property, property.propertyPath, source.Id));
                bool Query4243()
                {
                    var querySource4243 = children;
                    ArgumentNullException.ThrowIfNull(querySource4243, "source");
                    if (querySource4243.GetType() == typeof(global::BEngine.Editor.SerializedProperty[]) || querySource4243.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Editor.SerializedProperty>))
                    {
                        global::System.ReadOnlySpan<global::BEngine.Editor.SerializedProperty> queryValues4243 = querySource4243.GetType() == typeof(global::BEngine.Editor.SerializedProperty[]) ? (global::BEngine.Editor.SerializedProperty[])(object)querySource4243 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Editor.SerializedProperty>)(object)querySource4243);
                        foreach (var child in queryValues4243)
                        {
                            if (!(sourceSerialized.FindProperty(child.propertyPath)is not null))
                                return false;
                        }
                    }
                    else
                    {
                        foreach (var child in querySource4243)
                        {
                            if (!(sourceSerialized.FindProperty(child.propertyPath)is not null))
                                return false;
                        }
                    }
                
                    return true;
                }
            }
        }
    }

    public static void ApplyPropertyOverride(SerializedProperty property, string assetPath,
        InteractionMode action = InteractionMode.UserAction)
    {
        ArgumentNullException.ThrowIfNull(property);
        EditorAssetWritePolicy.EnsureCanWrite("Applying a prefab property");
        if (property.serializedObject.targetObjects.Length != 1) throw new ArgumentException("Apply one prefab target at a time.");
        using var context = new OverrideContext(property.serializedObject.targetObject, assetPath);
        EditInstance(context.Root, "Apply Prefab Property", action, () =>
        {
            var source = context.Source(property.serializedObject.targetObject);
            CopyProperty(property.serializedObject.targetObject, source, property.propertyPath, context.ToSource);
            context.Save(action);
        });
    }

    public static void RevertPropertyOverride(SerializedProperty property, InteractionMode action = InteractionMode.UserAction)
    {
        ArgumentNullException.ThrowIfNull(property);
        EditorAssetWritePolicy.EnsureCanWrite("Reverting a prefab property");
        using var context = new OverrideContext(property.serializedObject.targetObject);
        RevertProperty(context, property.serializedObject.targetObject, property.propertyPath, action);
    }

    public static void ApplyObjectOverride(BObject target, string assetPath, InteractionMode action = InteractionMode.UserAction)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Applying a prefab object");
        using var context = new OverrideContext(target, assetPath);
        EditInstance(context.Root, "Apply Prefab Object", action, () =>
        {
            CopyObject(target, context.Source(target), context.ToSource);
            context.Save(action);
        });
    }

    public static void RevertObjectOverride(BObject target, InteractionMode action = InteractionMode.UserAction)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Reverting a prefab object");
        using var context = new OverrideContext(target);
        EditInstance(context.Root, "Revert Prefab Object", action, () =>
        {
            var before = EditorPrefabEditSession.CaptureHierarchy(context.Root);
            CopyObject(context.Source(target), target, context.ToLive);
            EditorPrefabEditSession.AcceptChanges(context.Root, before, EditorPrefabEditSession.CaptureHierarchy(context.Root));
        });
    }

    public static void ApplyOverride(PrefabOverride item, InteractionMode action = InteractionMode.UserAction)
    {
        ArgumentNullException.ThrowIfNull(item);
        EnsurePrefabEditing(item.InstanceRoot);
        using var context = new OverrideContext(item.Target, GetPrefabAssetPathOfNearestInstanceRoot(item.InstanceRoot));
        EditInstance(context.Root, "Apply Prefab Override", action, () =>
        {
            switch (item.Kind)
            {
                case PrefabOverrideKind.Property:
                    CopyProperty(item.Target, context.Source(item.Target), item.PropertyPath, context.ToSource); break;
                case PrefabOverrideKind.AddedComponent:
                    var component = (Component)item.Target;
                    var added = context.AddSourceComponent(component);
                    CopyObject(component, added, context.ToSource); break;
                case PrefabOverrideKind.RemovedComponent:
                    var removed = (Component)context.SourceObject(item.SourceId);
                    if (!removed.gameObject.RemoveComponent(removed)) throw new InvalidOperationException("Another component requires this component.");
                    break;
                case PrefabOverrideKind.AddedGameObject:
                    context.CopySubtree((GameObject)item.Target, toSource: true, CopyObject); break;
                case PrefabOverrideKind.RemovedGameObject:
                    BObject.DestroyImmediate((GameObject)context.SourceObject(item.SourceId)); break;
                default: throw new ArgumentOutOfRangeException(nameof(item));
            }
            context.Save(action);
        });
    }

    public static void RevertOverride(PrefabOverride item, InteractionMode action = InteractionMode.UserAction)
    {
        ArgumentNullException.ThrowIfNull(item);
        EnsurePrefabEditing(item.InstanceRoot);
        using var context = new OverrideContext(item.Target, GetPrefabAssetPathOfNearestInstanceRoot(item.InstanceRoot));
        if (item.Kind == PrefabOverrideKind.Property) { RevertProperty(context, item.Target, item.PropertyPath, action); return; }
        EditInstance(context.Root, "Revert Prefab Override", action, () =>
        {
            var before = EditorPrefabEditSession.CaptureHierarchy(context.Root);
            switch (item.Kind)
            {
                case PrefabOverrideKind.AddedComponent:
                    var added = (Component)item.Target;
                    if (!added.gameObject.RemoveComponent(added)) throw new InvalidOperationException("Another component requires this component.");
                    break;
                case PrefabOverrideKind.RemovedComponent:
                    var source = (Component)context.SourceObject(item.SourceId);
                    var restored = context.RestoreInstanceComponent(source, (GameObject)item.Target);
                    CopyObject(source, restored, context.ToLive); break;
                case PrefabOverrideKind.AddedGameObject:
                    BObject.DestroyImmediate((GameObject)item.Target); break;
                case PrefabOverrideKind.RemovedGameObject:
                    context.CopySubtree((GameObject)context.SourceObject(item.SourceId), toSource: false, CopyObject); break;
                default: throw new ArgumentOutOfRangeException(nameof(item));
            }
            EditorPrefabEditSession.AcceptChanges(context.Root, before, EditorPrefabEditSession.CaptureHierarchy(context.Root));
        });
    }

    private static void RevertProperty(OverrideContext context, BObject target, string path, InteractionMode action) =>
        EditInstance(context.Root, "Revert Prefab Property", action, () =>
        {
            var before = EditorPrefabEditSession.CaptureHierarchy(context.Root);
            CopyProperty(context.Source(target), target, path, context.ToLive);
            EditorPrefabEditSession.AcceptChanges(context.Root, before, EditorPrefabEditSession.CaptureHierarchy(context.Root));
        });

    // Register every object before copying fields so sibling, child and cyclic references
    // resolve within the new subtree. Existing objects and their overrides are retained.
    private static void CopyProperty(BObject source, BObject target, string path, Func<BObject, BObject> mapper)
    {
        using var sourceObject = new SerializedObject(source); using var targetObject = new SerializedObject(target);
        var from = sourceObject.FindProperty(path) ?? throw new ArgumentException($"Unknown source property {path}.");
        var to = targetObject.FindProperty(path) ?? throw new ArgumentException($"Unknown target property {path}.");
        if (!to.editable) throw new InvalidOperationException($"Property {path} is read-only.");
        PropertyPath.Resolve(target, path).SetValue(EditorValueCloner.CloneValue(from.boxedValue, mapper, new(ReferenceEqualityComparer.Instance)));
        EditorUtility.SetDirty(target);
    }

    private static void CopyObject(BObject source, BObject target, Func<BObject, BObject> mapper)
    {
        using var sourceObject = new SerializedObject(source); using var targetObject = new SerializedObject(target);
        var visited = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        foreach (var path in PropertyPaths(source, sourceObject))
            if (sourceObject.FindProperty(path) is { } from && targetObject.FindProperty(path) is { editable: true } to)
                PropertyPath.Resolve(target, path).SetValue(EditorValueCloner.CloneValue(from.boxedValue, mapper, visited));
        EditorUtility.SetDirty(target);
    }

    private static IEnumerable<string> PropertyPaths(BObject target, SerializedObject serialized) => target switch
    {
        GameObject => ["name", "activeSelf", "tag", "layer", "isStatic"],
        Transform => serialized.GetPropertyPaths().Concat(["localPosition", "localRotation", "localScale"]).Distinct(),
        Component => serialized.GetPropertyPaths().Append("enabled").Distinct(),
        _ => serialized.GetPropertyPaths()
    };

    private static bool ValuesEqual(object? current, object? source, OverrideContext context,
        HashSet<(object, object)>? visited = null)
    {
        if (ReferenceEquals(current, source)) return true;
        if (current is null || source is null || current.GetType() != source.GetType()) return false;
        if (current is BObject engineObject && source is BObject sourceObject)
            return engineObject is GameObject or Component ? context.TrySource(engineObject, out var mapped) && mapped.Id == sourceObject.Id
                : engineObject.Id == sourceObject.Id;
        if (current is string || current.GetType().IsValueType) return current.Equals(source);
        visited ??= [];
        if (!visited.Add((current, source))) return true;
        if (current is IList list && source is IList sourceList)
            return list.Count == sourceList.Count && Enumerable.Range(0, list.Count).All(index => ValuesEqual(list[index], sourceList[index], context, visited));
        if (current is IDictionary dictionary && source is IDictionary sourceDictionary)
            return dictionary.Count == sourceDictionary.Count && dictionary.Keys.Cast<object>().All(key =>
                sourceDictionary.Contains(key) && ValuesEqual(dictionary[key], sourceDictionary[key], context, visited));
        return Query18165();
        bool Query18165()
        {
            var querySource18165 = SerializationMetadata.GetObjectMembers(current.GetType());
            ArgumentNullException.ThrowIfNull(querySource18165, "source");
            if (querySource18165.GetType() == typeof(global::System.Reflection.MemberInfo[]) || querySource18165.GetType() == typeof(global::System.Collections.Generic.List<global::System.Reflection.MemberInfo>))
            {
                global::System.ReadOnlySpan<global::System.Reflection.MemberInfo> queryValues18165 = querySource18165.GetType() == typeof(global::System.Reflection.MemberInfo[]) ? (global::System.Reflection.MemberInfo[])(object)querySource18165 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::System.Reflection.MemberInfo>)(object)querySource18165);
                foreach (var member in queryValues18165)
                {
                    if (!(ValuesEqual(RuntimeTypeCache.GetMemberAccessor(member).Getter(current), RuntimeTypeCache.GetMemberAccessor(member).Getter(source), context, visited)))
                        return false;
                }
            }
            else
            {
                foreach (var member in querySource18165)
                {
                    if (!(ValuesEqual(RuntimeTypeCache.GetMemberAccessor(member).Getter(current), RuntimeTypeCache.GetMemberAccessor(member).Getter(source), context, visited)))
                        return false;
                }
            }
        
            return true;
        }
    }

    private static void EditInstance(GameObject root, string name, InteractionMode action, Action edit)
    {
        EnsurePrefabEditing(root);
        var before = FullHierarchyState.Capture(root);
        try { edit(); }
        catch { before.CaptureInverse(); before.Restore(); throw; }
        var after = before.CaptureInverse();
        if (action == InteractionMode.UserAction) Undo.RegisterOperation(name, before.Restore, after.Restore, root);
        EditorUtility.SetDirty(root); RaiseInstanceUpdated(root);
    }

    private static void EnsurePrefabEditing(GameObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!EditorAssetWritePolicy.CanWrite || RuntimeAssetReferences.IsTransient(root))
            throw new InvalidOperationException("Prefab authoring is unavailable in Play Mode.");
    }

    private sealed class OverrideContext : EditorPrefabEditSession
    {
        internal string Path { get; }
        internal OverrideContext(BObject target, string? path = null) : this(ResolveSource(target, path)) { }
        private OverrideContext((GameObject Root, string Path, PrefabAsset Asset) source) : base(source.Root, source.Asset)
            => Path = source.Path;

        private static (GameObject Root, string Path, PrefabAsset Asset) ResolveSource(BObject target, string? path)
        {
            var root = GetNearestPrefabInstanceRoot(target) ?? throw new InvalidOperationException("Object is not inside a prefab instance.");
            if (path is not null)
            {
                var expected = ResolveAssetPath(path).FullPath;
                for (var current = ToGameObject(target); current is not null; current = current.transform.parent?.gameObject)
                    if (IsAnyPrefabInstanceRoot(current) && ResolveAssetPath(GetPrefabAssetPathOfNearestInstanceRoot(current)).FullPath.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    { root = current; break; }
                if (!ResolveAssetPath(GetPrefabAssetPathOfNearestInstanceRoot(root)).FullPath.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("The requested prefab is not an ancestor source of this instance.");
            }
            var resolved = GetPrefabAssetPathOfNearestInstanceRoot(root);
            return (root, resolved, LoadPrefabAsset(resolved));
        }

        internal void Save(InteractionMode action)
        {
            if (action == InteractionMode.UserAction) RegisterPrefabFileUndo(Path, "Apply Prefab Override");
            SaveAsset(Contents, Path, connect: false, out _);
            AcceptSourceChanges();
        }
    }

    private static void RegisterPrefabFileUndo(string path, string name)
    {
        var resolved = ResolveAssetPath(path);
        Undo.RegisterFileUndo(resolved.FullPath, resolved.ProjectPath, name);
    }
}
