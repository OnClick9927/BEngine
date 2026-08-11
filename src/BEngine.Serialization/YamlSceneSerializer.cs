using BEngine.Serialization.Documents;

namespace BEngine.Serialization;

public sealed class YamlSceneSerializer
{
    private readonly ComponentTypeRegistry _types;

    public YamlSceneSerializer(ComponentTypeRegistry? types = null) => _types = types ?? new ComponentTypeRegistry();

    public string Serialize(BEngine.Scene scene) => YamlUtility.Serialize(ToDocument(scene));

    public BEngine.Scene Deserialize(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);
        var document = YamlUtility.Deserialize<SceneDocument>(yaml);
        return FromDocument(document);
    }

    public BEngine.Scene Load(string path) => FromDocument(YamlUtility.Load<SceneDocument>(path));

    public void Save(BEngine.Scene scene, string path)
    {
        YamlUtility.Save(ToDocument(scene), path);
    }

    private static SceneDocument ToDocument(BEngine.Scene scene)
    {
        var document = new SceneDocument
        {
            Id = scene.Id,
            Name = scene.name
        };

        foreach (var gameObject in scene.gameObjects)
        {
            var item = new GameObjectDocument
            {
                Id = gameObject.Id,
                Name = gameObject.name,
                Active = gameObject.activeSelf,
                Parent = gameObject.transform.parent?.gameObject.Id,
                Transform = new TransformDocument
                {
                    Id = gameObject.transform.Id,
                    Type = gameObject.transform.GetType().FullName ?? typeof(BEngine.Transform).FullName!,
                    LocalPosition = new FixedVector3Document(gameObject.transform.localPosition),
                    LocalEulerAngles = new FixedVector3Document(gameObject.transform.localEulerAngles),
                    LocalScale = new FixedVector3Document(gameObject.transform.localScale),
                    Fields = SerializeDerivedTransformFields(gameObject.transform)
                }
            };

            foreach (var component in gameObject.components.Where(component => component is not BEngine.Transform))
            {
                item.Components.Add(new ComponentDocument
                {
                    Id = component.Id,
                    Type = component is BEngine.MissingComponent missing
                        ? missing.originalType
                        : component.GetType().FullName ?? component.GetType().Name,
                    Enabled = component.enabled,
                    Fields = ComponentFieldSerializer.Serialize(component)
                });
            }

            document.GameObjects.Add(item);
        }

        return document;
    }

    private BEngine.Scene FromDocument(SceneDocument document)
    {
        if (document.Format != "BEngine.Scene")
        {
            throw new InvalidDataException($"Unsupported document format '{document.Format}'.");
        }

        if (document.Version != 1)
        {
            throw new InvalidDataException($"Unsupported BEngine scene version {document.Version}.");
        }

        var scene = new BEngine.Scene(document.Name) { Id = document.Id };
        var objects = new Dictionary<Guid, BEngine.GameObject>();

        foreach (var item in document.GameObjects)
        {
            var gameObject = new BEngine.GameObject(item.Name)
            {
                Id = item.Id,
                activeSelf = item.Active
            };
            if (!string.IsNullOrWhiteSpace(item.Transform.Type) &&
                item.Transform.Type != typeof(BEngine.Transform).FullName)
            {
                var transformType = _types.Resolve(item.Transform.Type);
                if (transformType is null || !typeof(BEngine.Transform).IsAssignableFrom(transformType))
                {
                    throw new InvalidDataException($"Unknown Transform type '{item.Transform.Type}'.");
                }
                gameObject.AddComponent(transformType);
            }
            gameObject.transform.Id = item.Transform.Id;
            gameObject.transform.localPosition = item.Transform.LocalPosition.ToVector3();
            gameObject.transform.localEulerAngles = item.Transform.LocalEulerAngles.ToVector3();
            gameObject.transform.localScale = item.Transform.LocalScale.ToVector3();
            if (item.Transform.Fields.Count > 0)
            {
                ComponentFieldSerializer.Deserialize(gameObject.transform, item.Transform.Fields);
            }
            scene.Add(gameObject);
            objects.Add(item.Id, gameObject);

            foreach (var componentDocument in item.Components)
            {
                var type = _types.Resolve(componentDocument.Type);
                BEngine.Component component;
                if (type is null)
                {
                    component = gameObject.AddComponent<BEngine.MissingComponent>();
                    var missing = (BEngine.MissingComponent)component;
                    missing.originalType = componentDocument.Type;
                    missing.serializedFields = new Dictionary<string, string>(componentDocument.Fields, StringComparer.Ordinal);
                }
                else
                {
                    component = gameObject.AddComponent(type);
                    ComponentFieldSerializer.Deserialize(component, componentDocument.Fields);
                }

                component.Id = componentDocument.Id;
                component.enabled = componentDocument.Enabled;
            }
        }

        foreach (var item in document.GameObjects.Where(item => item.Parent.HasValue))
        {
            if (!objects.TryGetValue(item.Id, out var child) || !objects.TryGetValue(item.Parent!.Value, out var parent))
            {
                throw new InvalidDataException($"Scene hierarchy contains an unknown object reference for {item.Id}.");
            }

            child.transform.SetParent(parent.transform, worldPositionStays: false);
        }

        return scene;
    }

    private static Dictionary<string, string> SerializeDerivedTransformFields(BEngine.Transform transform)
    {
        if (transform.GetType() == typeof(BEngine.Transform)) return [];
        var fields = ComponentFieldSerializer.Serialize(transform);
        foreach (var member in ComponentFieldSerializer.GetSerializableMembers(typeof(BEngine.Transform)))
        {
            fields.Remove(member.Name);
        }
        return fields;
    }
}
