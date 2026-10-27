namespace BEngine.Rendering;

/// <summary>Shared quad submission API for optional renderer components. Valid only during
/// the current CollectGeometry callback; do not retain the sink between callbacks.</summary>
public interface ISceneGeometrySink2D
{
    RenderCamera Camera { get; }
    int Width { get; }
    int Height { get; }
    void Submit(Renderer2D renderer, SceneQuad2D quad, RenderBatchKey2D batchKey);
}

public readonly record struct SceneQuad2D(
    Vector2 Center, Fix64 Rotation, Vector2 Size,
    Color ColorA, Color ColorB, Color ColorC, Color ColorD,
    string Texture, Rect Uv, bool FlipX, bool FlipY, bool IsTextured)
{
    public object? EffectData { get; init; }
    /// <summary>Optional world-space half-axes preserve non-uniform hierarchical scale and shear.</summary>
    public Vector2? WorldAxisX { get; init; }
    public Vector2? WorldAxisY { get; init; }
    public SceneQuad2D WithTransform(AffineTransform2D transform)
    {
        GetWorldAxes(out var x, out var y);
        return this with
        {
            Center = transform.TransformPoint(Center),
            WorldAxisX = transform.TransformVector(x),
            WorldAxisY = transform.TransformVector(y)
        };
    }
    public void GetWorldAxes(out Vector2 x, out Vector2 y)
    {
        x = WorldAxisX ?? Transform.RotateVector(new Vector2(Size.x * Fix64.Half, 0), Rotation);
        y = WorldAxisY ?? Transform.RotateVector(new Vector2(0, Size.y * Fix64.Half), Rotation);
    }
    /// <summary>Custom unlit pipelines may opt out of lighting and other surface transformations.</summary>
    public bool ApplySurfaceEffects { get; init; } = true;

    public static SceneQuad2D Create(Vector2 center, Fix64 rotation, Vector2 size,
        Color color, string texture, Rect uv, bool flipX, bool flipY, bool isTextured) =>
        new(center, rotation, size, color, color, color, color, texture, uv, flipX, flipY, isTextured);
}

public sealed class SceneQuadPayload2D
{
    private FrameDataBuffer<SceneQuad2D>? _values;
    private int _index;
    // Package-facing facade; renderer-owned geometry itself lives in a contiguous value buffer.
    public SceneQuad2D Value
    {
        get => _values is { } values && (uint)_index < (uint)values.Count ? values[_index] : default;
        set
        {
            if (_values is null) { _values = new(1); _index = _values.Add(value); }
            else _values[_index] = value;
        }
    }
    /// <summary>Reads the current effect tag without returning a complete quad value.</summary>
    public object? EffectData =>
        _values is { } values && (uint)_index < (uint)values.Count ? values[_index].EffectData : null;

    internal void Bind(FrameDataBuffer<SceneQuad2D> values, int index) { _values = values; _index = index; }
    internal void Unbind() { _values = null; _index = 0; }
}
