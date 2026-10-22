using BEngine.Rendering;

namespace BEngine;

public sealed partial class LineRenderer2D
{
    private readonly List<LineSegment2D> _renderSegments = [];
    private long _cachedGeometryVersion = -1;
    private long _cachedTransformVersion = -1;
    private Fix64 _cachedOpacity;
    private bool _cachedWorldSpace;

    private void EnsureSegments()
    {
        var transformVersion = useWorldSpace ? 0 : transform.worldVersion;
        if (_cachedGeometryVersion == GeometryVersion && _cachedTransformVersion == transformVersion &&
            _cachedOpacity == opacity && _cachedWorldSpace == useWorldSpace) return;
        _renderSegments.Clear();
        FillSegments(_renderSegments);
        _cachedGeometryVersion = GeometryVersion;
        _cachedTransformVersion = transformVersion;
        _cachedOpacity = opacity;
        _cachedWorldSpace = useWorldSpace;
    }

    public override void CollectGeometry(ISceneGeometrySink2D sink)
    {
        if (!RuntimePackageState.IsEnabled("com.bengine.line-rendering2d")) return;
        EnsureSegments();
        foreach (var segment in _renderSegments)
        {
            var color = new Color(segment.Color.r, segment.Color.g, segment.Color.b, segment.Color.a * opacity);
            sink.Submit(this, SceneQuad2D.Create(segment.Center, segment.Rotation, segment.Size, color,
                string.Empty, new Rect(0, 0, 1, 1), false, false, false), new RenderBatchKey2D(material, string.Empty));
        }
    }
}
