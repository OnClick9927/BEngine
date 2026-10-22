using BEngine.Serialization;

namespace BEngine;

[EditorIcon("Icons/Assets/AssetImage.png")]
[CreateAssetMenu(fileName = "New Sprite Skin", menuName = "2D/Sprite Skin", order = 255)]
public sealed class SpriteSkinAsset : ScriptableObject
{
    public string Format { get; set; } = "BEngine.SpriteSkin2D";
    public int Version { get; set; } = 1;
    public List<SpriteSkinBone> Bones { get; set; } =
    [
        new() { Name = "Root", ParentIndex = -1, BindScale = Vector2.one, Length = Fix64.One }
    ];
    public List<SpriteSkinVertex> Vertices { get; set; } =
    [
        DefaultVertex("-0.5", "-0.5", 0, 1), DefaultVertex("0.5", "-0.5", 1, 1),
        DefaultVertex("0.5", "0.5", 1, 0), DefaultVertex("-0.5", "0.5", 0, 0)
    ];
    public List<int> Triangles { get; set; } = [0, 1, 2, 0, 2, 3];
    public List<SpriteSkinAttachment> Attachments { get; set; } =
    [
        new() { Name = "Default", Texture = "missing:sprite-skin", Uv = new Rect(0, 0, 1, 1) }
    ];

    public SpriteSkinAttachment? FindAttachment(string name) =>
        Attachments.FirstOrDefault(item => item.Name.Equals(name?.Trim(), StringComparison.Ordinal));

    public void SetAttachment(string name, string texture, Rect uv, Color tint)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0) throw new ArgumentException("An attachment name is required.", nameof(name));
        var attachment = FindAttachment(name);
        if (attachment is null)
        {
            attachment = new SpriteSkinAttachment { Name = name };
            Attachments.Add(attachment);
        }
        attachment.Texture = texture?.Replace('\\', '/').Trim() ?? string.Empty;
        attachment.Uv = uv;
        attachment.Tint = tint;
    }

    public void SetAttachment(string name, Sprite sprite, Color? tint = null)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        var visual = SpriteSourceRegistry.Resolve(sprite);
        SetAttachment(name, visual.Texture, visual.Uv, tint ?? Color.white);
    }

    public static SpriteSkinAsset CreateDefault(string name = "New Sprite Skin") => new() { name = name };

    public void Validate()
    {
        if (!Format.Equals("BEngine.SpriteSkin2D", StringComparison.Ordinal) || Version != 1)
            throw new InvalidDataException($"Unsupported sprite skin format '{Format}' version {Version}.");
        Bones ??= []; Vertices ??= []; Triangles ??= []; Attachments ??= [];
        if (Bones.Count is < 1 or > 256) throw new InvalidDataException("A sprite skin requires 1 to 256 bones.");
        if (Vertices.Count is < 3 or > 65535) throw new InvalidDataException("A sprite skin requires 3 to 65535 vertices.");
        if (Triangles.Count < 3 || Triangles.Count % 3 != 0)
            throw new InvalidDataException("Sprite skin triangle indices must contain complete triangles.");
        if (Attachments.Count == 0) throw new InvalidDataException("A sprite skin requires at least one attachment.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Bones.Count; index++)
        {
            var bone = Bones[index] ?? throw new InvalidDataException($"Bone {index} is null.");
            bone.Name = bone.Name?.Trim() ?? string.Empty;
            if (bone.Name.Length == 0 || !names.Add(bone.Name))
                throw new InvalidDataException($"Bone {index} has an empty or duplicate name '{bone.Name}'.");
            if (bone.ParentIndex >= index || bone.ParentIndex < -1)
                throw new InvalidDataException($"Bone '{bone.Name}' must reference an earlier parent or -1.");
            if (bone.BindScale.x == 0 || bone.BindScale.y == 0)
                throw new InvalidDataException($"Bone '{bone.Name}' has a zero bind scale.");
            bone.Length = Fix64.Max(Fix64.Parse("0.001"), bone.Length);
        }
        for (var index = 0; index < Vertices.Count; index++)
            Vertices[index].Validate(index, Bones.Count);
        foreach (var triangle in Triangles)
            if ((uint)triangle >= (uint)Vertices.Count)
                throw new InvalidDataException($"Triangle index {triangle} is outside the vertex array.");
        for (var index = 0; index < Triangles.Count; index += 3)
        {
            var a = Vertices[Triangles[index]].Position;
            var b = Vertices[Triangles[index + 1]].Position;
            var c = Vertices[Triangles[index + 2]].Position;
            if ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) == 0)
                throw new InvalidDataException($"Triangle {index / 3} has zero area.");
        }
        var attachments = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attachment in Attachments)
        {
            attachment.Name = attachment.Name?.Trim() ?? string.Empty;
            attachment.Texture = attachment.Texture?.Replace('\\', '/').Trim() ?? string.Empty;
            if (attachment.Name.Length == 0 || !attachments.Add(attachment.Name))
                throw new InvalidDataException($"Attachment name '{attachment.Name}' is empty or duplicated.");
            if (attachment.Texture.Length == 0)
                throw new InvalidDataException($"Attachment '{attachment.Name}' has no texture.");
            if (attachment.Uv.width <= 0 || attachment.Uv.height <= 0)
                throw new InvalidDataException($"Attachment '{attachment.Name}' has an empty UV rectangle.");
        }
    }

    public static SpriteSkinAsset Load(string path) => Document<SpriteSkinAsset>
        .Read(path, static source =>
        {
            return Restore(YamlUtility.Load<SpriteSkinAssetData>(source));
        }).ToAsset();

    internal static SpriteSkinAsset Deserialize(string yaml) => Document<SpriteSkinAsset>
        .Parse(yaml, static text =>
        {
            return Restore(YamlUtility.Deserialize<SpriteSkinAssetData>(text));
        }).ToAsset();

    public void Save(string path) => Document<SpriteSkinAsset>.FromAsset(this).Write(path,
        static (asset, destination) =>
        {
            asset.Validate();
            YamlUtility.Save(Capture(asset), destination);
        });

    private static SpriteSkinAsset Restore(SpriteSkinAssetData data)
    {
        if (!data.Format.Equals("BEngine.SpriteSkin2D", StringComparison.Ordinal) || data.Version != 1)
            throw new InvalidDataException($"Unsupported sprite skin format '{data.Format}' version {data.Version}.");
        var asset = new SpriteSkinAsset
        {
            name = data.Name,
            Format = data.Format,
            Version = data.Version,
            Bones = data.Bones.Select(item => new SpriteSkinBone
            {
                Name = item.Name,
                ParentIndex = item.ParentIndex,
                BindPosition = Vector(item.BindPosition),
                BindRotation = Fix64.FromRaw(item.BindRotation),
                BindScale = Vector(item.BindScale),
                Length = Fix64.FromRaw(item.Length)
            }).ToList(),
            Vertices = data.Vertices.Select(item => new SpriteSkinVertex
            {
                Position = Vector(item.Position),
                Uv = Vector(item.Uv),
                Weights = item.Weights.Select(weight => new SpriteSkinWeight
                {
                    BoneIndex = weight.BoneIndex,
                    Weight = Fix64.FromRaw(weight.Weight)
                }).ToList()
            }).ToList(),
            Triangles = data.Triangles.ToList(),
            Attachments = data.Attachments.Select(item => new SpriteSkinAttachment
            {
                Name = item.Name,
                Texture = item.Texture,
                Uv = new Rect(Fix64.FromRaw(item.Uv.X), Fix64.FromRaw(item.Uv.Y),
                    Fix64.FromRaw(item.Uv.Width), Fix64.FromRaw(item.Uv.Height)),
                Tint = new Color(Fix64.FromRaw(item.Tint.R), Fix64.FromRaw(item.Tint.G),
                    Fix64.FromRaw(item.Tint.B), Fix64.FromRaw(item.Tint.A))
            }).ToList()
        };
        asset.Validate();
        return asset;
    }

    private static SpriteSkinAssetData Capture(SpriteSkinAsset asset) => new()
    {
        Name = asset.name,
        Format = asset.Format,
        Version = asset.Version,
        Bones = asset.Bones.Select(item => new SpriteSkinBoneData
        {
            Name = item.Name,
            ParentIndex = item.ParentIndex,
            BindPosition = Vector(item.BindPosition),
            BindRotation = item.BindRotation.RawValue,
            BindScale = Vector(item.BindScale),
            Length = item.Length.RawValue
        }).ToList(),
        Vertices = asset.Vertices.Select(item => new SpriteSkinVertexData
        {
            Position = Vector(item.Position),
            Uv = Vector(item.Uv),
            Weights = item.Weights.Select(weight => new SpriteSkinWeightData
            {
                BoneIndex = weight.BoneIndex,
                Weight = weight.Weight.RawValue
            }).ToList()
        }).ToList(),
        Triangles = asset.Triangles.ToList(),
        Attachments = asset.Attachments.Select(item => new SpriteSkinAttachmentData
        {
            Name = item.Name,
            Texture = item.Texture,
            Uv = new SpriteSkinRectData
            {
                X = item.Uv.x.RawValue,
                Y = item.Uv.y.RawValue,
                Width = item.Uv.width.RawValue,
                Height = item.Uv.height.RawValue
            },
            Tint = new SpriteSkinColorData
            {
                R = item.Tint.r.RawValue,
                G = item.Tint.g.RawValue,
                B = item.Tint.b.RawValue,
                A = item.Tint.a.RawValue
            }
        }).ToList()
    };

    private static Vector2 Vector(SpriteSkinVectorData value) =>
        new(Fix64.FromRaw(value.X), Fix64.FromRaw(value.Y));

    private static SpriteSkinVectorData Vector(Vector2 value) =>
        new() { X = value.x.RawValue, Y = value.y.RawValue };

    private static SpriteSkinVertex DefaultVertex(string x, string y, int u, int v) => new()
    {
        Position = new Vector2(Fix64.Parse(x), Fix64.Parse(y)),
        Uv = new Vector2(u, v),
        Weights = [new SpriteSkinWeight { BoneIndex = 0, Weight = Fix64.One }]
    };
}

internal sealed class SpriteSkinAssetData
{
    public string Format { get; set; } = "BEngine.SpriteSkin2D";
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "New Sprite Skin";
    public List<SpriteSkinBoneData> Bones { get; set; } = [];
    public List<SpriteSkinVertexData> Vertices { get; set; } = [];
    public List<int> Triangles { get; set; } = [];
    public List<SpriteSkinAttachmentData> Attachments { get; set; } = [];
}

internal sealed class SpriteSkinBoneData
{
    public string Name { get; set; } = "Bone";
    public int ParentIndex { get; set; } = -1;
    public SpriteSkinVectorData BindPosition { get; set; } = new();
    public long BindRotation { get; set; }
    public SpriteSkinVectorData BindScale { get; set; } = new() { X = Fix64.OneRaw, Y = Fix64.OneRaw };
    public long Length { get; set; } = Fix64.OneRaw;
}

internal sealed class SpriteSkinVertexData
{
    public SpriteSkinVectorData Position { get; set; } = new();
    public SpriteSkinVectorData Uv { get; set; } = new();
    public List<SpriteSkinWeightData> Weights { get; set; } = [];
}

internal sealed class SpriteSkinWeightData
{
    public int BoneIndex { get; set; }
    public long Weight { get; set; } = Fix64.OneRaw;
}

internal sealed class SpriteSkinAttachmentData
{
    public string Name { get; set; } = "Default";
    public string Texture { get; set; } = string.Empty;
    public SpriteSkinRectData Uv { get; set; } = new()
    {
        Width = Fix64.OneRaw,
        Height = Fix64.OneRaw
    };
    public SpriteSkinColorData Tint { get; set; } = new()
    {
        R = Fix64.OneRaw,
        G = Fix64.OneRaw,
        B = Fix64.OneRaw,
        A = Fix64.OneRaw
    };
}

internal sealed class SpriteSkinVectorData
{
    public long X { get; set; }
    public long Y { get; set; }
}

internal sealed class SpriteSkinRectData
{
    public long X { get; set; }
    public long Y { get; set; }
    public long Width { get; set; }
    public long Height { get; set; }
}

internal sealed class SpriteSkinColorData
{
    public long R { get; set; }
    public long G { get; set; }
    public long B { get; set; }
    public long A { get; set; }
}

public sealed class SpriteSkinBone
{
    public string Name { get; set; } = "Bone";
    public int ParentIndex { get; set; } = -1;
    public Vector2 BindPosition { get; set; }
    public Fix64 BindRotation { get; set; }
    public Vector2 BindScale { get; set; } = Vector2.one;
    public Fix64 Length { get; set; } = Fix64.One;
}

public sealed class SpriteSkinVertex
{
    public Vector2 Position { get; set; }
    public Vector2 Uv { get; set; }
    public List<SpriteSkinWeight> Weights { get; set; } = [];

    internal void Validate(int vertexIndex, int boneCount)
    {
        Weights ??= [];
        // Valid normalized input is the usual render path. Preserve it without rebuilding LINQ groups.
        var normalized = Weights.Count is > 0 and <= 4;
        var sum = Fix64.Zero;
        for (var i = 0; normalized && i < Weights.Count; i++)
        {
            var item = Weights[i];
            if (item is null || item.Weight <= 0 || (uint)item.BoneIndex >= (uint)boneCount) { normalized = false; break; }
            sum += item.Weight;
            if (i > 0 && item.Weight > Weights[i - 1].Weight) normalized = false;
            for (var j = 0; j < i; j++) if (Weights[j].BoneIndex == item.BoneIndex) normalized = false;
        }
        if (normalized && sum == Fix64.One) return;
        var combined = Weights.Where(weight => weight is not null && weight.Weight > 0)
            .GroupBy(weight => weight.BoneIndex)
            .Select(group => new SpriteSkinWeight
            {
                BoneIndex = group.Key,
                Weight = group.Aggregate(Fix64.Zero, (sum, item) => sum + item.Weight)
            })
            .OrderByDescending(weight => weight.Weight).Take(4).ToList();
        if (combined.Count == 0 || combined.Any(weight => (uint)weight.BoneIndex >= (uint)boneCount))
            throw new InvalidDataException($"Vertex {vertexIndex} has no valid bone weights.");
        var total = combined.Aggregate(Fix64.Zero, (sum, weight) => sum + weight.Weight);
        if (total <= Fix64.Epsilon) throw new InvalidDataException($"Vertex {vertexIndex} has zero total weight.");
        foreach (var weight in combined) weight.Weight /= total;
        Weights = combined;
    }
}

public sealed class SpriteSkinWeight
{
    public int BoneIndex { get; set; }
    public Fix64 Weight { get; set; } = Fix64.One;
}

public sealed class SpriteSkinAttachment
{
    public string Name { get; set; } = "Default";
    public string Texture { get; set; } = string.Empty;
    public Rect Uv { get; set; } = new(0, 0, 1, 1);
    public Color Tint { get; set; } = Color.white;
}
