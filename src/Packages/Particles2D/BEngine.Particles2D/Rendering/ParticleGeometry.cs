using BEngine.Rendering;

namespace BEngine;

public sealed partial class ParticleSystem2D
{
    public override void CollectGeometry(ISceneGeometrySink2D sink)
    {
        if (!RuntimePackageState.IsEnabled("com.bengine.particles2d")) return;
        CollectTrails(sink);
        var camera = sink.Camera;
        var visual = ResolveSpriteUnchecked();
        transform.GetWorldPoseUnchecked(
            out var systemPosition, out var systemRotation, out var systemScale);
        var sizeScale = simulationSpace == ParticleSimulationSpace.World ? Vector2.one : systemScale;
        // Keep List<T>'s version checks without boxing its struct enumerator
        // through the public IReadOnlyList<T> view on every render.
        foreach (var particle in _particles)
        {
            var worldPosition = simulationSpace == ParticleSimulationSpace.World
                ? particle.Position
                : systemPosition + Transform.RotateVector(
                    Vector2.Scale(systemScale, particle.Position), systemRotation);
            var color = WithOpacity(particle.Color, opacity);
            var rotation = simulationSpace == ParticleSimulationSpace.World
                ? particle.Rotation : systemRotation + particle.Rotation;
            var offset = new Vector2(
                (Fix64.Half - visual.Pivot.x) * particle.Size.x,
                (Fix64.Half - visual.Pivot.y) * particle.Size.y);
            worldPosition += Transform.RotateVector(
                Vector2.Scale(offset, sizeScale), rotation);
            var renderedSize = Vector2.Scale(particle.Size, sizeScale);
            var quad = SceneQuad2D.Create(camera.SnapWorldPoint(worldPosition), rotation,
                Abs(renderedSize), color,
                visual.Texture, visual.Uv, renderedSize.x < 0, renderedSize.y < 0,
                visual.IsTextured);
            sink.Submit(this, quad, BatchKey(visual));
        }

    }
    private static Color WithOpacity(Color color, Fix64 opacity) => new(color.r, color.g, color.b, color.a * opacity);
    private static Vector2 Abs(Vector2 value) => new(Fix64.Abs(value.x), Fix64.Abs(value.y));
}
