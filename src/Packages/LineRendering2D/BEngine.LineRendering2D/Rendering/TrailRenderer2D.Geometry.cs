using BEngine.Rendering;

namespace BEngine;

public sealed partial class TrailRenderer2D
{
    private readonly List<LineSegment2D> _renderSegments = [];
    public override void CollectGeometry(ISceneGeometrySink2D sink)
    {
        if (!RuntimePackageState.IsEnabled("com.bengine.line-rendering2d")) return;
        _renderSegments.Clear();
        FillSegments(_renderSegments);
        foreach (var segment in _renderSegments)
        {
            var color = new Color(segment.Color.r, segment.Color.g, segment.Color.b, segment.Color.a * opacity);
            sink.Submit(this, SceneQuad2D.Create(segment.Center, segment.Rotation, segment.Size, color,
                string.Empty, new Rect(0, 0, 1, 1), false, false, false), new RenderBatchKey2D(material, string.Empty));
        }
    }
}
