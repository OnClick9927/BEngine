namespace BEngine;

public enum SpriteSkinningMode { Cpu, Gpu, Auto }

public readonly record struct SpriteSkinBoneTransform(Vector2 Origin, Vector2 XAxis, Vector2 YAxis)
{
    public Vector2 TransformPoint(Vector2 point) => Origin + XAxis * point.x + YAxis * point.y;
}

[AddComponentMenu("Rendering/Sprite Skin 2D")]
public sealed class SpriteSkin2D : Renderer2D
{
    public SpriteSkinAsset? skin { get; set; }
    public Transform? rootBone { get; set; }
    public Transform[] bones { get; set; } = [];
    public string attachment { get; set; } = "Default";
    public Color color { get; set; } = Color.white;
    public Vector2[] deformation { get; set; } = [];
    public SpriteSkinningMode skinningMode { get; set; } = SpriteSkinningMode.Auto;

    public SpriteSkinBoneTransform[] GetWorldBoneTransforms() => GetWorldBoneTransformsCore(true);
    internal SpriteSkinBoneTransform[] GetWorldBoneTransformsValidated() => GetWorldBoneTransformsCore(false);
    private SpriteSkinBoneTransform[] GetWorldBoneTransformsCore(bool validate)
    {
        var asset = skin ?? throw new InvalidOperationException("SpriteSkin2D has no skin asset.");
        if (validate) asset.Validate();
        if (bones.Length != asset.Bones.Count || bones.Any(item => item is null))
            if (!Rebind()) throw new InvalidOperationException("SpriteSkin2D bone bindings are incomplete.");
        var result = new SpriteSkinBoneTransform[bones.Length];
        for (var index = 0; index < bones.Length; index++)
        {
            var bind = asset.Bones[index]; var bone = bones[index];
            var origin = Map(Vector2.zero);
            result[index] = new(origin, Map(new(1, 0)) - origin, Map(new(0, 1)) - origin);
            Vector2 Map(Vector2 position)
            {
                var relative = Transform.RotateVector(position - bind.BindPosition, -bind.BindRotation);
                return bone.TransformPoint(new(relative.x / bind.BindScale.x, relative.y / bind.BindScale.y));
            }
        }
        return result;
    }

    public IReadOnlyList<string> attachments => skin?.Attachments.Select(item => item.Name).ToArray() ?? [];

    public bool SetAttachment(string name)
    {
        if (skin?.FindAttachment(name) is null) return false;
        attachment = name.Trim();
        return true;
    }

    public void SetDeformation(ReadOnlySpan<Vector2> offsets)
    {
        if (skin is null) throw new InvalidOperationException("SpriteSkin2D has no skin asset.");
        if (offsets.Length != skin.Vertices.Count)
            throw new ArgumentException("Deformation offset count must match the skin vertex count.", nameof(offsets));
        deformation = offsets.ToArray();
    }

    public void ClearDeformation() => deformation = [];

    public bool Rebind()
    {
        if (skin is null || rootBone is null) { bones = []; return false; }
        var byName = Descendants(rootBone).GroupBy(item => item.gameObject.name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var resolved = new Transform[skin.Bones.Count];
        for (var index = 0; index < resolved.Length; index++)
            if (!byName.TryGetValue(skin.Bones[index].Name, out resolved[index]!))
            { bones = resolved; return false; }
        bones = resolved;
        return true;
    }

    public Vector2[] GetDeformedLocalVertices() => GetDeformedLocalVerticesCore(true);
    private Vector2[] GetDeformedLocalVerticesCore(bool validate)
    {
        var asset = skin ?? throw new InvalidOperationException("SpriteSkin2D has no skin asset.");
        if (validate) asset.Validate();
        if (bones.Length != asset.Bones.Count || bones.Any(item => item is null))
            if (!Rebind()) throw new InvalidOperationException("SpriteSkin2D bone bindings are incomplete.");
        var result = new Vector2[asset.Vertices.Count];
        for (var vertexIndex = 0; vertexIndex < result.Length; vertexIndex++)
        {
            var vertex = asset.Vertices[vertexIndex];
            var value = Vector2.zero;
            foreach (var influence in vertex.Weights)
            {
                var bind = asset.Bones[influence.BoneIndex];
                var deformedPosition = vertex.Position +
                    (vertexIndex < deformation.Length ? deformation[vertexIndex] : Vector2.zero);
                var relative = Transform.RotateVector(deformedPosition - bind.BindPosition, -bind.BindRotation);
                relative = new Vector2(relative.x / bind.BindScale.x, relative.y / bind.BindScale.y);
                var world = bones[influence.BoneIndex].TransformPoint(relative);
                value += transform.InverseTransformPoint(world) * influence.Weight;
            }
            result[vertexIndex] = value;
        }
        return result;
    }

    public Vector2[] GetDeformedWorldVertices() => WorldVertices(true);
    internal Vector2[] GetDeformedWorldVerticesValidated() => WorldVertices(false);
    private Vector2[] WorldVertices(bool validate)
    {
        var vertices = GetDeformedLocalVerticesCore(validate);
        for (var index = 0; index < vertices.Length; index++) vertices[index] = transform.TransformPoint(vertices[index]);
        return vertices;
    }

    public SpriteSkinAttachment ResolveAttachment()
    {
        var asset = skin ?? throw new InvalidOperationException("SpriteSkin2D has no skin asset.");
        return asset.FindAttachment(attachment) ?? asset.Attachments.FirstOrDefault() ??
               throw new InvalidOperationException("Sprite skin has no attachments.");
    }

    public override void OnValidate()
    {
        if (skin is null) return;
        skin.Validate();
        if (skin.FindAttachment(attachment) is null && skin.Attachments.Count > 0)
            attachment = skin.Attachments[0].Name;
    }

    private static IEnumerable<Transform> Descendants(Transform root)
    {
        var pending = new Stack<Transform>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;
            for (var index = current.childCount - 1; index >= 0; index--)
                pending.Push(current.GetChild(index));
        }
    }
}
