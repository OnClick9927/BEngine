using System.Reflection;
using BEngine.Documents;
using YamlDotNet.Serialization;

namespace BEngine.Serialization;

internal static class SceneAssetSerialization
{
    private static readonly ComponentTypeRegistry Types = new();

    internal static Scene Load(string path, IServiceProvider? services = null) =>
        Document<Scene>.Read(path, sourcePath => Restore(YamlUtility.Load<SceneAssetData>(sourcePath), services))
            .ToAsset();

    internal static Scene Deserialize(string yaml, IServiceProvider? services = null) =>
        Document<Scene>.Parse(yaml, contents => Restore(YamlUtility.Deserialize<SceneAssetData>(contents), services))
            .ToAsset();

    internal static Scene Clone(Scene source, IServiceProvider? services = null) =>
        Deserialize(Serialize(source), services);

    internal static string Serialize(Scene scene) => Document<Scene>.FromAsset(scene)
        .Serialize(asset => YamlUtility.Serialize(Capture(asset)));

    internal static void Save(Scene scene, string path) => Document<Scene>.FromAsset(scene)
        .Write(path, static (asset, destination) => YamlUtility.Save(Capture(asset), destination));

    internal static void UnregisterAssembly(Assembly assembly) => Types.UnregisterAssembly(assembly);

    internal static SceneAssetData Capture(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return new SceneAssetData
        {
            IsRuntimeSnapshot = scene.IsRuntimeOnly || SceneRuntime.IsRunningScene(scene),
            Id = scene.Id,
            Name = scene.name,
            GameObjects = [.. scene.gameObjects.Select(FromGameObject)]
        };
    }

    internal static GameObjectData FromGameObject(GameObject gameObject)
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
                Fields = SerializeDerivedTransformFields(gameObject.transform)
            }
        };

        foreach (var component in gameObject.components.Where(component => component is not Transform))
        {
            SerializationCallbackUtility.BeforeSerialize(component);
            document.Components.Add(new ComponentData
            {
                Id = component.Id,
                Type = component is MissingComponent missing
                    ? missing.originalType
                    : component.GetType().FullName ?? component.GetType().Name,
                Enabled = component.enabled,
                PrefabAsset = component.PrefabAssetId,
                PrefabSource = component.PrefabSourceId,
                Fields = ComponentFieldSerializer.Serialize(component)
            });
        }

        return document;
    }

    internal static Scene Restore(SceneAssetData document, IServiceProvider? services = null)
    {
        AssetDataValidation.ValidateScene(document);
        var scene = new Scene(document.Name, services)
        {
            Id = document.Id,
            IsRuntimeOnly = document.IsRuntimeSnapshot
        };
        var objects = new Dictionary<Guid, GameObject>();

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
            RestoreTransform(gameObject.transform, item.Transform);
            scene.Add(gameObject);
            objects.Add(item.Id, gameObject);
            RestoreComponents(gameObject, item.Components);
        }

        foreach (var item in document.GameObjects.Where(item => item.Parent.HasValue))
        {
            if (!objects.TryGetValue(item.Id, out var child) ||
                !objects.TryGetValue(item.Parent!.Value, out var parent))
                throw new InvalidDataException($"Scene hierarchy contains an unknown object reference for {item.Id}.");
            child.transform.SetParent(parent.transform, worldPositionStays: false);
        }

        return scene;
    }

    private static void RestoreTransformType(GameObject gameObject, TransformData document)
    {
        if (string.IsNullOrWhiteSpace(document.Type) || document.Type == typeof(Transform).FullName) return;
        var transformType = Types.Resolve(document.Type);
        if (transformType is null || !typeof(Transform).IsAssignableFrom(transformType))
            throw new InvalidDataException($"Unknown Transform type '{document.Type}'.");
        gameObject.AddComponent(transformType);
    }

    private static void RestoreTransform(Transform transform, TransformData document)
    {
        transform.Id = document.Id;
        transform.PrefabAssetId = document.PrefabAsset;
        transform.PrefabSourceId = document.PrefabSource;
        transform.localPosition = document.LocalPosition.ToVector2();
        transform.localRotation = Fix64.Parse(document.LocalRotation);
        transform.localScale = document.LocalScale.ToVector2();
        if (document.Fields.Count > 0) ComponentFieldSerializer.Deserialize(transform, document.Fields);
        SerializationCallbackUtility.AfterDeserialize(transform);
    }

    private static void RestoreComponents(GameObject gameObject, IEnumerable<ComponentData> documents)
    {
        foreach (var document in documents)
        {
            var type = Types.Resolve(document.Type);
            Component component;
            if (type is null)
            {
                var missing = gameObject.AddComponent<MissingComponent>();
                missing.originalType = document.Type;
                missing.serializedFields = new Dictionary<string, string>(document.Fields, StringComparer.Ordinal);
                component = missing;
            }
            else
            {
                component = gameObject.AddComponent(type);
                ComponentFieldSerializer.Deserialize(component, document.Fields);
            }

            component.Id = document.Id;
            component.enabled = document.Enabled;
            component.PrefabAssetId = document.PrefabAsset;
            component.PrefabSourceId = document.PrefabSource;
            SerializationCallbackUtility.AfterDeserialize(component);
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
}
