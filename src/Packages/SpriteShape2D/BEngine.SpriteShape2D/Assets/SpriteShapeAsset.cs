using BEngine.Serialization;
using BEngine.Physics2D;

namespace BEngine;

public enum SpriteShapeTangentMode
{
    Linear,
    Free,
    Mirrored
}

public sealed class SpriteShapePoint
{
    public Vector2 Position { get; set; }
    public Vector2 LeftTangent { get; set; }
    public Vector2 RightTangent { get; set; }
    public SpriteShapeTangentMode Mode { get; set; } = SpriteShapeTangentMode.Linear;
}

public sealed class SpriteShapeHole
{
    public List<Vector2> Points { get; set; } = [];
}

public sealed class SpriteShapeCornerRule
{
    public Fix64 MinimumAngle { get; set; } = 30;
    public Fix64 MaximumAngle { get; set; } = 150;
    public bool InnerCorner { get; set; }
    public string Texture { get; set; } = "missing:sprite-shape-corner";
    public Rect Uv { get; set; } = new(0, 0, 1, 1);
    public Vector2 Size { get; set; } = new(Fix64.Half, Fix64.Half);
    public Color Color { get; set; } = Color.white;
}

[EditorIcon("Icons/Assets/AssetImage.png")]
[CreateAssetMenu(fileName = "New Sprite Shape", menuName = "2D/Sprite Shape", order = 256)]
public sealed class SpriteShapeAsset : ScriptableObject
{
    public string Format { get; set; } = "BEngine.SpriteShape2D";
    public int Version { get; set; } = 1;
    public bool Closed { get; set; } = true;
    public int SamplesPerSegment { get; set; } = 12;
    public bool AdaptiveTessellation { get; set; }
    public Fix64 FlatnessTolerance { get; set; } = Fix64.Parse("0.01");
    public int MaxSubdivisionDepth { get; set; } = 8;
    public Fix64 EdgeWidth { get; set; } = Fix64.Parse("0.2");
    public Fix64 EdgeRepeatLength { get; set; } = Fix64.One;
    public string EdgeTexture { get; set; } = "missing:sprite-shape-edge";
    public Rect EdgeUv { get; set; } = new(0, 0, 1, 1);
    public Color EdgeColor { get; set; } = Color.white;
    public string FillTexture { get; set; } = "missing:sprite-shape-fill";
    public Rect FillUv { get; set; } = new(0, 0, 1, 1);
    public Color FillColor { get; set; } = Color.white;
    public List<SpriteShapePoint> Points { get; set; } =
    [
        new() { Position = new Vector2(-1, -1) },
        new() { Position = new Vector2(1, -1) },
        new() { Position = new Vector2(1, 1) },
        new() { Position = new Vector2(-1, 1) }
    ];
    public List<SpriteShapeHole> Holes { get; set; } = [];
    public List<SpriteShapeCornerRule> CornerRules { get; set; } = [];

    public static SpriteShapeAsset CreateDefault(string name = "New Sprite Shape") => new() { name = name };

    public void Validate()
    {
        if (!Format.Equals("BEngine.SpriteShape2D", StringComparison.Ordinal) || Version != 1)
            throw new InvalidDataException($"Unsupported sprite shape format '{Format}' version {Version}.");
        Points ??= []; Holes ??= []; CornerRules ??= [];
        var minimum = Closed ? 3 : 2;
        if (Points.Count < minimum || Points.Count > 4096)
            throw new InvalidDataException($"A {(Closed ? "closed" : "open")} sprite shape requires {minimum} to 4096 points.");
        if (SamplesPerSegment is < 1 or > 128)
            throw new InvalidDataException("Samples per spline segment must be between 1 and 128.");
        if (FlatnessTolerance <= 0 || FlatnessTolerance > 10 || MaxSubdivisionDepth is < 1 or > 16)
            throw new InvalidDataException("Adaptive tessellation requires a tolerance in (0, 10] and depth from 1 to 16.");
        if (!Closed && Holes.Count > 0) throw new InvalidDataException("Only closed sprite shapes can contain fill holes.");
        if (EdgeWidth <= 0 || EdgeRepeatLength <= 0)
            throw new InvalidDataException("Edge width and repeat length must be positive.");
        EdgeTexture = NormalizePath(EdgeTexture);
        FillTexture = NormalizePath(FillTexture);
        if (EdgeTexture.Length == 0) throw new InvalidDataException("The edge texture is required.");
        if (Closed && FillTexture.Length == 0) throw new InvalidDataException("A closed shape requires a fill texture.");
        if (EdgeUv.width <= 0 || EdgeUv.height <= 0 || FillUv.width <= 0 || FillUv.height <= 0)
            throw new InvalidDataException("Sprite shape UV rectangles must have positive size.");
        for (var index = 0; index < Points.Count; index++)
        {
            var point = Points[index] ?? throw new InvalidDataException($"Sprite shape point {index} is null.");
            if (!Enum.IsDefined(point.Mode)) throw new InvalidDataException($"Point {index} has an invalid tangent mode.");
            if (point.Mode == SpriteShapeTangentMode.Linear)
                point.LeftTangent = point.RightTangent = Vector2.zero;
            else if (point.Mode == SpriteShapeTangentMode.Mirrored)
                point.LeftTangent = -point.RightTangent;
        }
        var sampled = SpriteShapeGeometryBuilder.Sample(this);
        if (sampled.Zip(sampled.Skip(1), Vector2.Distance).Any(length => length <= Fix64.Epsilon))
            throw new InvalidDataException("Sprite shape contains a zero-length sampled edge.");
        if (Closed && Fix64.Abs(SpriteShapeGeometryBuilder.SignedArea(sampled)) <= Fix64.Parse("0.000001"))
            throw new InvalidDataException("Closed sprite shape has zero area.");
        foreach (var (hole, index) in Holes.Select((value, index) => (value, index)))
        {
            hole.Points ??= [];
            if (hole.Points.Count is < 3 or > 4096)
                throw new InvalidDataException($"Sprite shape hole {index} requires 3 to 4096 points.");
            if (Fix64.Abs(SpriteShapeGeometryBuilder.SignedArea(hole.Points)) <= Fix64.Parse("0.000001"))
                throw new InvalidDataException($"Sprite shape hole {index} has zero area.");
        }
        foreach (var rule in CornerRules)
        {
            rule.Texture = NormalizePath(rule.Texture);
            if (rule.MinimumAngle < 0 || rule.MaximumAngle > 180 || rule.MinimumAngle > rule.MaximumAngle ||
                rule.Texture.Length == 0 || rule.Size.x <= 0 || rule.Size.y <= 0 || rule.Uv.width <= 0 || rule.Uv.height <= 0)
                throw new InvalidDataException("Sprite shape corner rules require angles in [0,180], positive size/UV and a texture.");
        }
    }

    public void SetTangent(int index, bool left, Vector2 value)
    {
        var point = Points[index];
        if (point.Mode == SpriteShapeTangentMode.Linear) point.Mode = SpriteShapeTangentMode.Free;
        if (left) point.LeftTangent = value; else point.RightTangent = value;
        if (point.Mode == SpriteShapeTangentMode.Mirrored)
        {
            if (left) point.RightTangent = -value; else point.LeftTangent = -value;
        }
    }

    /// <summary>Bakes both splines into polygon contours and returns a separate styled asset for each resulting island.</summary>
    public IReadOnlyList<SpriteShapeAsset> Combine(SpriteShapeAsset other, PolygonBooleanOperation2D operation)
    {
        ArgumentNullException.ThrowIfNull(other);
        Validate(); other.Validate();
        if (!Closed || !other.Closed) throw new InvalidOperationException("Boolean operations require two closed sprite shapes.");
        var contours = PolygonGeometry2D.Combine(Paths(this), Paths(other), operation);
        var result = new List<SpriteShapeAsset>();
        foreach (var outer in contours.Where(path => !path.isHole))
        {
            var asset = Restore(Capture(this));
            asset.name = name + " " + operation + " " + (result.Count + 1);
            asset.Points = outer.points.Select(point => new SpriteShapePoint { Position = point }).ToList();
            asset.Holes = contours.Where(path => path.isHole && Contains(outer.points, path.points[0]))
                .Select(path => new SpriteShapeHole { Points = path.points.ToList() }).ToList();
            asset.Validate(); result.Add(asset);
        }
        return result;

        static List<ColliderPath2D> Paths(SpriteShapeAsset asset)
        {
            var paths = new List<ColliderPath2D> { new() { points = SpriteShapeGeometryBuilder.Sample(asset).ToList() } };
            paths.AddRange(asset.Holes.Select(hole => new ColliderPath2D { isHole = true, points = hole.Points.ToList() }));
            return paths;
        }
        static bool Contains(IReadOnlyList<Vector2> contour, Vector2 point)
        {
            var inside = false;
            for (int index = 0, previous = contour.Count - 1; index < contour.Count; previous = index++)
            {
                var a = contour[index]; var b = contour[previous];
                if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }
    }

    public static SpriteShapeAsset Load(string path) => Document<SpriteShapeAsset>.Read(path,
        static source => Restore(YamlUtility.Load<SpriteShapeAssetData>(source))).ToAsset();

    internal static SpriteShapeAsset Deserialize(string yaml) => Document<SpriteShapeAsset>.Parse(yaml,
        static source => Restore(YamlUtility.Deserialize<SpriteShapeAssetData>(source))).ToAsset();

    public void Save(string path) => Document<SpriteShapeAsset>.FromAsset(this).Write(path,
        static (asset, destination) =>
        {
            asset.Validate();
            YamlUtility.Save(Capture(asset), destination);
        });

    private static SpriteShapeAsset Restore(SpriteShapeAssetData data)
    {
        var asset = new SpriteShapeAsset
        {
            name = data.Name,
            Format = data.Format,
            Version = data.Version,
            Closed = data.Closed,
            SamplesPerSegment = data.SamplesPerSegment,
            AdaptiveTessellation = data.AdaptiveTessellation,
            FlatnessTolerance = Fix64.FromRaw(data.FlatnessTolerance),
            MaxSubdivisionDepth = data.MaxSubdivisionDepth,
            EdgeWidth = Fix64.FromRaw(data.EdgeWidth),
            EdgeRepeatLength = Fix64.FromRaw(data.EdgeRepeatLength),
            EdgeTexture = data.EdgeTexture,
            EdgeUv = Rect(data.EdgeUv),
            EdgeColor = RestoreColor(data.EdgeColor),
            FillTexture = data.FillTexture,
            FillUv = Rect(data.FillUv),
            FillColor = RestoreColor(data.FillColor),
            Points = data.Points.Select(item => new SpriteShapePoint
            {
                Position = Vector(item.Position),
                LeftTangent = Vector(item.LeftTangent),
                RightTangent = Vector(item.RightTangent),
                Mode = item.Mode
            }).ToList(),
            Holes = data.Holes.Select(hole => new SpriteShapeHole
            {
                Points = hole.Points.Select(Vector).ToList()
            }).ToList(),
            CornerRules = data.CornerRules.Select(rule => new SpriteShapeCornerRule
            {
                MinimumAngle = Fix64.FromRaw(rule.MinimumAngle), MaximumAngle = Fix64.FromRaw(rule.MaximumAngle),
                InnerCorner = rule.InnerCorner, Texture = rule.Texture, Uv = Rect(rule.Uv),
                Size = Vector(rule.Size), Color = RestoreColor(rule.Color)
            }).ToList()
        };
        asset.Validate();
        return asset;
    }

    private static SpriteShapeAssetData Capture(SpriteShapeAsset asset) => new()
    {
        Name = asset.name,
        Format = asset.Format,
        Version = asset.Version,
        Closed = asset.Closed,
        SamplesPerSegment = asset.SamplesPerSegment,
        AdaptiveTessellation = asset.AdaptiveTessellation,
        FlatnessTolerance = asset.FlatnessTolerance.RawValue,
        MaxSubdivisionDepth = asset.MaxSubdivisionDepth,
        EdgeWidth = asset.EdgeWidth.RawValue,
        EdgeRepeatLength = asset.EdgeRepeatLength.RawValue,
        EdgeTexture = asset.EdgeTexture,
        EdgeUv = Rect(asset.EdgeUv),
        EdgeColor = CaptureColor(asset.EdgeColor),
        FillTexture = asset.FillTexture,
        FillUv = Rect(asset.FillUv),
        FillColor = CaptureColor(asset.FillColor),
        Points = asset.Points.Select(item => new SpriteShapePointData
        {
            Position = Vector(item.Position),
            LeftTangent = Vector(item.LeftTangent),
            RightTangent = Vector(item.RightTangent),
            Mode = item.Mode
        }).ToList(),
        Holes = asset.Holes.Select(hole => new SpriteShapeHoleData
        {
            Points = hole.Points.Select(Vector).ToList()
        }).ToList(),
        CornerRules = asset.CornerRules.Select(rule => new SpriteShapeCornerRuleData
        {
            MinimumAngle = rule.MinimumAngle.RawValue, MaximumAngle = rule.MaximumAngle.RawValue,
            InnerCorner = rule.InnerCorner, Texture = rule.Texture, Uv = Rect(rule.Uv),
            Size = Vector(rule.Size), Color = CaptureColor(rule.Color)
        }).ToList()
    };

    private static string NormalizePath(string? path) => path?.Replace('\\', '/').Trim() ?? string.Empty;
    private static Vector2 Vector(SpriteShapeVectorData value) => new(Fix64.FromRaw(value.X), Fix64.FromRaw(value.Y));
    private static SpriteShapeVectorData Vector(Vector2 value) => new() { X = value.x.RawValue, Y = value.y.RawValue };
    private static Rect Rect(SpriteShapeRectData value) => new(Fix64.FromRaw(value.X), Fix64.FromRaw(value.Y), Fix64.FromRaw(value.Width), Fix64.FromRaw(value.Height));
    private static SpriteShapeRectData Rect(Rect value) => new() { X = value.x.RawValue, Y = value.y.RawValue, Width = value.width.RawValue, Height = value.height.RawValue };
    private static Color RestoreColor(SpriteShapeColorData value) => new(Fix64.FromRaw(value.R), Fix64.FromRaw(value.G), Fix64.FromRaw(value.B), Fix64.FromRaw(value.A));
    private static SpriteShapeColorData CaptureColor(Color value) => new() { R = value.r.RawValue, G = value.g.RawValue, B = value.b.RawValue, A = value.a.RawValue };
}

internal sealed class SpriteShapeAssetData
{
    public string Format { get; set; } = "BEngine.SpriteShape2D";
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "New Sprite Shape";
    public bool Closed { get; set; } = true;
    public int SamplesPerSegment { get; set; } = 12;
    public bool AdaptiveTessellation { get; set; }
    public long FlatnessTolerance { get; set; } = Fix64.OneRaw / 100;
    public int MaxSubdivisionDepth { get; set; } = 8;
    public long EdgeWidth { get; set; } = Fix64.OneRaw / 5;
    public long EdgeRepeatLength { get; set; } = Fix64.OneRaw;
    public string EdgeTexture { get; set; } = "missing:sprite-shape-edge";
    public SpriteShapeRectData EdgeUv { get; set; } = SpriteShapeRectData.Full;
    public SpriteShapeColorData EdgeColor { get; set; } = SpriteShapeColorData.White;
    public string FillTexture { get; set; } = "missing:sprite-shape-fill";
    public SpriteShapeRectData FillUv { get; set; } = SpriteShapeRectData.Full;
    public SpriteShapeColorData FillColor { get; set; } = SpriteShapeColorData.White;
    public List<SpriteShapePointData> Points { get; set; } = [];
    public List<SpriteShapeHoleData> Holes { get; set; } = [];
    public List<SpriteShapeCornerRuleData> CornerRules { get; set; } = [];
}

internal sealed class SpriteShapeCornerRuleData
{
    public long MinimumAngle { get; set; }
    public long MaximumAngle { get; set; } = 180 * Fix64.OneRaw;
    public bool InnerCorner { get; set; }
    public string Texture { get; set; } = "missing:sprite-shape-corner";
    public SpriteShapeRectData Uv { get; set; } = SpriteShapeRectData.Full;
    public SpriteShapeVectorData Size { get; set; } = new() { X = Fix64.OneRaw / 2, Y = Fix64.OneRaw / 2 };
    public SpriteShapeColorData Color { get; set; } = SpriteShapeColorData.White;
}

internal sealed class SpriteShapeHoleData
{
    public List<SpriteShapeVectorData> Points { get; set; } = [];
}

internal sealed class SpriteShapePointData
{
    public SpriteShapeVectorData Position { get; set; } = new();
    public SpriteShapeVectorData LeftTangent { get; set; } = new();
    public SpriteShapeVectorData RightTangent { get; set; } = new();
    public SpriteShapeTangentMode Mode { get; set; }
}

internal sealed class SpriteShapeVectorData { public long X { get; set; } public long Y { get; set; } }
internal sealed class SpriteShapeRectData
{
    public long X { get; set; } public long Y { get; set; }
    public long Width { get; set; } public long Height { get; set; }
    public static SpriteShapeRectData Full => new() { Width = Fix64.OneRaw, Height = Fix64.OneRaw };
}
internal sealed class SpriteShapeColorData
{
    public long R { get; set; } public long G { get; set; } public long B { get; set; } public long A { get; set; }
    public static SpriteShapeColorData White => new() { R = Fix64.OneRaw, G = Fix64.OneRaw, B = Fix64.OneRaw, A = Fix64.OneRaw };
}
