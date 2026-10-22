using BEngine.Rendering;

namespace BEngine.ShaderGraph;

public sealed class ShaderParameterValue
{
    public string name { get; set; } = "Tint";
    public Color value { get; set; } = Color.white;
}

[AddComponentMenu("Rendering/Shader Graph Renderer 2D")]
public sealed class ShaderGraphRenderer2D : Renderer2D, ISpriteReferenceOwner
{
    public ShaderGraphRenderer2D() { receiveLighting = true; }
    public ShaderGraphAsset? graph { get; set; }
    public ShaderGraphMaterial? shaderMaterial { get; set; }
    public Sprite? sprite { get; set; }
    public Vector2 size { get; set; } = Vector2.one;
    public Color color { get; set; } = Color.white;
    public bool flipX { get; set; }
    public bool flipY { get; set; }
    public Fix64 timeScale { get; set; } = 1;
    public Fix64 timeOffset { get; set; }
    public List<ShaderParameterValue> parameters { get; set; } = [];
    public List<ShaderTextureValue> textures { get; set; } = [];

    public ShaderGraphAsset? ResolveGraph() => graph is { isDestroyed: false } ? graph :
        shaderMaterial is { isDestroyed: false } ? shaderMaterial.graph : null;

    public void SetTexture(string name, Texture? texture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var item = textures.FirstOrDefault(item => item.name == name);
        if (item is null) textures.Add(new() { name = name, texture = texture });
        else item.texture = texture;
    }

    public void SetParameter(string name, Color value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var parameter = parameters.FirstOrDefault(item => item.name == name);
        if (parameter is null) parameters.Add(new() { name = name, value = value });
        else parameter.value = value;
    }

    public override void CollectGeometry(ISceneGeometrySink2D sink)
    {
        if (ResolveGraph() is not { isDestroyed: false } || !RuntimePackageState.IsEnabled("com.bengine.shader-graph")) return;
        var visual = SpriteSourceRegistry.Resolve(sprite);
        var scale = transform.lossyScale;
        var dimensions = Vector2.Scale(size, scale);
        var offset = Vector2.Scale(new Vector2(Fix64.Half - visual.Pivot.x, Fix64.Half - visual.Pivot.y), size);
        var quad = SceneQuad2D.Create(sink.Camera.SnapWorldPoint(transform.TransformPoint(offset)),
            transform.rotation, new(Fix64.Abs(dimensions.x), Fix64.Abs(dimensions.y)),
            new(color.r, color.g, color.b, color.a * opacity), visual.Texture, visual.Uv,
            flipX ^ (dimensions.x < 0), flipY ^ (dimensions.y < 0), visual.IsTextured) with { EffectData = this, ApplySurfaceEffects = false };
        var matrix = transform.localToWorldMatrix;
        var axisX = matrix.TransformVector(new Vector2(Fix64.Abs(size.x) * Fix64.Half, 0));
        var axisY = matrix.TransformVector(new Vector2(0, Fix64.Abs(size.y) * Fix64.Half));
        if (scale.x < 0) axisX = -axisX;
        if (scale.y < 0) axisY = -axisY;
        quad = quad with { WorldAxisX = axisX, WorldAxisY = axisY };
        // Only the shared default unlit state can merge. Independent overrides retain their identity.
        var shared = ResolveGraph() is { surface: ShaderGraphSurface.Unlit } current &&
            shaderMaterial is null && parameters.Count == 0 && textures.Count == 0 && timeScale == 1 && timeOffset == 0
            ? current.Id : Id;
        sink.Submit(this, quad, new RenderBatchKey2D(shared, "BEngine/ShaderGraph", visual.BatchIdentity));
    }
}
