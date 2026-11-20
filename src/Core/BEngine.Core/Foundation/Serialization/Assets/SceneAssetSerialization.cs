using System.Reflection;
using BEngine.Serialization;

namespace BEngine.Serialization;

internal static class SceneAssetSerialization
{
    private static readonly ComponentTypeRegistry Types = new();

    private static ISceneDocumentCodec Codec => DependencyInjection.RuntimeServiceContributions.GetDefault<ISceneDocumentCodec>() ?? throw new InvalidOperationException("No scene document codec is registered.");
    internal static Scene Load(string path, IServiceProvider? services = null) => Codec.Load(path, services);
    internal static Scene Deserialize(string text, IServiceProvider? services = null) => Codec.Deserialize(text, services);
    internal static string Serialize(Scene scene) => Codec.Serialize(scene);
    internal static void Save(Scene scene, string path) => Codec.Save(scene, path);

    internal static Scene Clone(Scene source, IServiceProvider? services = null) =>
        Restore(Capture(source, allowTransientObjects: true), services, resolvePrefabs: false);

    internal static void UnregisterAssembly(Assembly assembly) => Types.UnregisterAssembly(assembly);

    internal static SceneAssetData Capture(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return Capture(scene, allowTransientObjects:
            scene.IsRuntimeOnly || SceneRuntime.IsRunningScene(scene));
    }

    internal static SceneAssetData Capture(Scene scene, bool allowTransientObjects)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return new SceneAssetData
        {
            IsRuntimeSnapshot = scene.IsRuntimeOnly || SceneRuntime.IsRunningScene(scene),
            Id = scene.Id,
            Name = scene.name,
            GameObjects = [.. scene.gameObjects.Select(gameObject =>
                FromGameObject(gameObject, allowTransientObjects))],
            PrefabInstances = scene.gameObjects.Where(item => item.PrefabInstance is not null)
                .Select(item => AssetSnapshotClone.Clone(item.PrefabInstance!)).ToList()
        };
    }

    internal static GameObjectData FromGameObject(
        GameObject gameObject,
        bool allowTransientObjects = false)
    {
        SerializationCallbackUtility.BeforeSerialize(gameObject.transform);
        var document = new GameObjectData
        {
            Id = gameObject.Id,
            Name = gameObject.name,
            Active = gameObject.activeSelf,
            Tag = gameObject.tag,
            Layer = gameObject.layer,
            IsStatic = gameObject.isStatic,
            Parent = gameObject.transform.parent?.gameObject.Id,
            PrefabAsset = gameObject.PrefabAssetId,
            PrefabSource = gameObject.PrefabSourceId,
            Transform = new TransformData
            {
                Id = gameObject.transform.Id,
                Type = gameObject.transform.GetType().FullName ?? typeof(Transform).FullName!,
                PrefabAsset = gameObject.transform.PrefabAssetId,
                PrefabSource = gameObject.transform.PrefabSourceId,
                LocalPosition = new FixedVector2Data(gameObject.transform.localPosition),
                LocalRotation = gameObject.transform.localRotation.ToString(),
                LocalScale = new FixedVector2Data(gameObject.transform.localScale),
                Fields = SerializeDerivedTransformFields(gameObject.transform),
                Graph = SerializeDerivedTransformGraph(gameObject.transform, allowTransientObjects)
            }
        };

        {
            var __filteredSource3535 = gameObject.components;
            ArgumentNullException.ThrowIfNull(__filteredSource3535, "source");
            foreach (var component in __filteredSource3535)
            {
                if (!(component is not Transform))
                    continue;
                SerializationCallbackUtility.BeforeSerialize(component);
                document.Components.Add(new ComponentData { Id = component.Id, Type = component is MissingComponent missing ? missing.originalType : component.GetType().FullName ?? component.GetType().Name, Enabled = component.enabled, PrefabAsset = component.PrefabAssetId, PrefabSource = component.PrefabSourceId, Fields = ComponentFieldSerializer.Serialize(component), Graph = ComponentObjectGraphSerializer.Capture(component, allowTransientObjects: allowTransientObjects) });
            }
        }

        return document;
    }

    internal static Scene Restore(SceneAssetData document, IServiceProvider? services = null, bool resolvePrefabs = true,
        Func<Guid, BObject?>? externalReferenceResolver = null, IResourceObjectProvider? resources = null)
    {
        Scene? scene = null;
        try
        {
            using var work = RestoreSteps(document, services, resolvePrefabs, externalReferenceResolver,
                value => scene = value, resources).GetEnumerator();
            while (work.MoveNext()) { }
            return scene!;
        }
        catch
        {
            if (scene is { isCreated: true }) scene.Dispose();
            throw;
        }
    }

    /// <summary>Restores objects, hierarchy and bindings on the owner thread under the shared frame budget.</summary>
    internal static async BTask<Scene> RestoreAsync(SceneAssetData document, IServiceProvider? services,
        AsyncLoadContext context, bool resolvePrefabs = true, Func<Guid, BObject?>? externalReferenceResolver = null,
        IResourceObjectProvider? resources = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.cancellationToken.ThrowIfCancellationRequested();
        Scene? scene = null;
        var succeeded = false;
        try
        {
            var references = ((((global::System.Collections.Generic.IEnumerable<string>)(EnumerateAssetReferences(document))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.OrdinalIgnoreCase)) switch
            {
                (string[] { Length: 0 }, _) => global::System.Array.Empty<string>(),
                var (distinctItems5656, distinctComparer5656) => distinctItems5656.ToHashSet<string>(distinctComparer5656).ToArray()});
            for (var index = 0; index < references.Length; index++)
            {
                var referenceIndex = index;
                var preparation = new AsyncLoadContext(context.cancellationToken,
                    value => context.ReportProgress(.15f * (referenceIndex + value) / references.Length))
                    { priority = context.priority };
                await Resources.PrepareReferenceAsync(references[index], preparation);
                context.ReportProgress(.15f * (index + 1) / references.Length);
            }
            using var work = RestoreSteps(document, services, resolvePrefabs, externalReferenceResolver,
                value => scene = value, resources).GetEnumerator();
            var completed = 0;
            var componentCount = Scan6598(document.GameObjects);
            var estimatedSteps = Math.Max(1, 1 + document.GameObjects.Count * 3 + componentCount + document.PrefabInstances.Count);
            if (resources is not null) estimatedSteps += document.GameObjects.Count + componentCount;
            [global::BEngine.SkipLoopGuard]
            int Scan6598(global::System.Collections.Generic.IEnumerable<global::BEngine.Serialization.GameObjectData> scanSource6598)
            {
                ArgumentNullException.ThrowIfNull(scanSource6598, "source");
                int total = 0;
                foreach (var scanItem6598 in scanSource6598)
                    total = checked(total + Match6598(scanItem6598));
                return total;
                int Match6598(global::BEngine.Serialization.GameObjectData item) => item.Components.Count;
            }
            await context.RunSteps(() =>
            {
                if (!work.MoveNext()) return CooperativeWorkStatus.Complete;
                context.ReportProgress(.15f + .84f * Math.Min(1, (float)++completed / estimatedSteps));
                return CooperativeWorkStatus.Continue;
            });
            context.cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress(1);
            succeeded = true;
            return scene!;
        }
        finally
        {
            if (!succeeded && scene is { isCreated: true }) scene.Dispose();
        }
    }

    private static IEnumerable<string> EnumerateAssetReferences(SceneAssetData document)
    {
        foreach (var item in document.GameObjects)
        {
            if (Types.Resolve(item.Transform.Type) is { } transformType)
                foreach (var reference in item.Transform.Graph is { } transformGraph
                    ? ComponentObjectGraphSerializer.EnumerateAssetReferences(transformType, transformGraph)
                    : ComponentFieldSerializer.EnumerateAssetReferences(transformType, item.Transform.Fields))
                    yield return reference;
            foreach (var component in item.Components)
            {
                if (Types.Resolve(component.Type) is not { } type) continue;
                foreach (var reference in component.Graph is { } graph
                    ? ComponentObjectGraphSerializer.EnumerateAssetReferences(type, graph)
                    : ComponentFieldSerializer.EnumerateAssetReferences(type, component.Fields))
                    yield return reference;
            }
        }
    }

    private static IEnumerable<object?> RestoreSteps(SceneAssetData document, IServiceProvider? services,
        bool resolvePrefabs, Func<Guid, BObject?>? externalReferenceResolver, Action<Scene> created,
        IResourceObjectProvider? resources)
    {
        AssetDataValidation.ValidateScene(document);
        if (resolvePrefabs && document.PrefabInstances.Count > 0)
        {
            var runtimeSnapshot = document.IsRuntimeSnapshot;
            document = AssetSnapshotClone.Clone(document);
            document.IsRuntimeSnapshot = runtimeSnapshot;
            PrefabResolver.ResolveReferences(document.GameObjects, document.PrefabInstances);
            AssetDataValidation.ValidateScene(document);
        }
        var scene = new Scene(document.Name, services)
        {
            Id = document.Id,
            IsRuntimeOnly = document.IsRuntimeSnapshot
        };
        created(scene);
        yield return null;
        var objects = new Dictionary<Guid, GameObject>(document.GameObjects.Count);
        var references = new Dictionary<Guid, BObject>();
        var componentBindings = new List<(Component Component, ComponentData Data)>();
        var transformBindings = new List<(Transform Transform, TransformData Data)>();

        foreach (var item in document.GameObjects)
        {
            var gameObject = new GameObject(item.Name)
            {
                Id = item.Id,
                activeSelf = item.Active,
                tag = string.IsNullOrWhiteSpace(item.Tag) ? "Untagged" : item.Tag,
                layer = item.Layer,
                isStatic = item.IsStatic,
                PrefabAssetId = item.PrefabAsset,
                PrefabSourceId = item.PrefabSource
            };
            RestoreTransformType(gameObject, item.Transform);
            RestoreTransformIdentity(gameObject.transform, item.Transform);
            scene.Add(gameObject);
            objects.Add(item.Id, gameObject);
            references.Add(item.Id, gameObject);
            references.Add(gameObject.transform.Id, gameObject.transform);
            transformBindings.Add((gameObject.transform, item.Transform));
            CreateComponents(gameObject, item.Components, references, componentBindings);
            yield return null;
        }

        {
            var __filteredSource10689 = document.GameObjects;
            ArgumentNullException.ThrowIfNull(__filteredSource10689, "source");
            foreach (var item in __filteredSource10689)
            {
                if (!(item.Parent.HasValue))
                    continue;
                if (!objects.TryGetValue(item.Id, out var child) || !objects.TryGetValue(item.Parent!.Value, out var parent))
                    throw new InvalidDataException($"Scene hierarchy contains an unknown object reference for {item.Id}.");
                child.transform.SetParent(parent.transform, worldPositionStays: false);
                yield return null;
            }
        }

        BObject? ResolveReference(Guid id) => references.GetValueOrDefault(id) ?? externalReferenceResolver?.Invoke(id);
        foreach (var (transform, data) in transformBindings)
        {
            if (data.Graph is not null)
                ComponentObjectGraphSerializer.Restore(transform, data.Graph, ResolveReference);
            else if (data.Fields.Count > 0)
                ComponentFieldSerializer.Deserialize(transform, data.Fields);
            SerializationCallbackUtility.AfterDeserialize(transform);
            yield return null;
        }
        foreach (var (component, data) in componentBindings)
        {
            if (data.Graph is not null)
                ComponentObjectGraphSerializer.Restore(component, data.Graph, ResolveReference);
            else
                ComponentFieldSerializer.Deserialize(component, data.Fields);
            SerializationCallbackUtility.AfterDeserialize(component);
            yield return null;
        }

        foreach (var reference in document.PrefabInstances)
        {
            if (objects.TryGetValue(reference.Root, out var root)) root.PrefabInstance = AssetSnapshotClone.Clone(reference);
            yield return null;
        }

        if (resources is not null)
        {
            foreach (var (transform, data) in transformBindings)
            {
                SceneReferenceRestorers.Restore(transform, data.Fields, resources);
                yield return null;
            }
            foreach (var (component, data) in componentBindings)
            {
                SceneReferenceRestorers.Restore(component, data.Fields, resources);
                yield return null;
            }
        }
    }

    private static void RestoreTransformType(GameObject gameObject, TransformData document)
    {
        if (string.IsNullOrWhiteSpace(document.Type) || document.Type == typeof(Transform).FullName) return;
        var transformType = Types.Resolve(document.Type);
        if (transformType is null || !typeof(Transform).IsAssignableFrom(transformType))
            throw new InvalidDataException($"Unknown Transform type '{document.Type}'.");
        gameObject.AddComponent(transformType);
    }

    private static void RestoreTransformIdentity(Transform transform, TransformData document)
    {
        transform.Id = document.Id;
        transform.PrefabAssetId = document.PrefabAsset;
        transform.PrefabSourceId = document.PrefabSource;
        transform.localPosition = document.LocalPosition.ToVector2();
        transform.localRotation = Fix64.Parse(document.LocalRotation);
        transform.localScale = document.LocalScale.ToVector2();
    }

    private static void CreateComponents(
        GameObject gameObject,
        IEnumerable<ComponentData> documents,
        IDictionary<Guid, BObject> references,
        ICollection<(Component Component, ComponentData Data)> bindings)
    {
        var assigned = new HashSet<Component>(ReferenceEqualityComparer.Instance);
        foreach (var document in documents)
        {
            var type = Types.Resolve(document.Type);
            Component component;
            if (type is null)
            {
                var missing = gameObject.AddComponent<MissingComponent>();
                missing.originalType = document.Type;
                missing.serializedFields = new Dictionary<string, string>(document.Fields, StringComparer.Ordinal);
                missing.serializedGraph = document.Graph is null
                    ? null
                    : ComponentObjectGraphSerializer.Clone(document.Graph);
                component = missing;
            }
            else
            {
                // RequireComponent may have created a component whose saved record comes later.
                component = Matches14762() ?? gameObject.AddComponent(type);
                [global::BEngine.SkipLoopGuard]
                global::BEngine.Component? Matches14762()
                {
                    var matchSource14762 = gameObject.components;
                    ArgumentNullException.ThrowIfNull(matchSource14762, "source");
                    global::System.ReadOnlySpan<global::BEngine.Component> matchValues14762;
                    if (matchSource14762.GetType() == typeof(global::BEngine.Component[]))
                        matchValues14762 = (global::BEngine.Component[])(object)matchSource14762;
                    else if (matchSource14762.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Component>))
                        matchValues14762 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Component>)(object)matchSource14762);
                    else
                    {
                        foreach (var candidate in (global::System.Collections.Generic.IEnumerable<global::BEngine.Component>)matchSource14762)
                        {
                            if (candidate is not Transform && candidate.GetType() == type && !assigned.Contains(candidate))
                                return candidate;
                        }
                
                        return default!;
                    }
                
                    foreach (var candidate in matchValues14762)
                    {
                        if (candidate is not Transform && candidate.GetType() == type && !assigned.Contains(candidate))
                            return candidate;
                    }
                
                    return default!;
                }
            }

            assigned.Add(component);
            component.Id = document.Id;
            component.enabled = document.Enabled;
            component.PrefabAssetId = document.PrefabAsset;
            component.PrefabSourceId = document.PrefabSource;
            references.Add(component.Id, component);
            bindings.Add((component, document));
        }
    }

    private static Dictionary<string, string> SerializeDerivedTransformFields(Transform transform)
    {
        if (transform.GetType() == typeof(Transform)) return [];
        var fields = ComponentFieldSerializer.Serialize(transform);
        foreach (var member in ComponentFieldSerializer.GetSerializableMembers(typeof(Transform)))
            fields.Remove(member.Name);
        return fields;
    }

    private static ComponentGraphData? SerializeDerivedTransformGraph(
        Transform transform,
        bool allowTransientObjects)
    {
        if (transform.GetType() == typeof(Transform)) return null;
        var baseMemberNames = ComponentFieldSerializer.GetSerializableMembers(typeof(Transform))
            .Select(member => member.Name).ToHashSet(StringComparer.Ordinal);
        var members = ComponentFieldSerializer.GetSerializableMembers(transform.GetType())
            .Where(member => !baseMemberNames.Contains(member.Name));
        return ComponentObjectGraphSerializer.Capture(
            transform, members, allowTransientObjects);
    }
}
