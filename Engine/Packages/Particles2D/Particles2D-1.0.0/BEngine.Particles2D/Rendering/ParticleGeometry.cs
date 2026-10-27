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
            // Keep each live instance and submission interleaved. Camera policy
            // and sink callbacks can mutate the system before the next item.
            var quad = WorldQuadGeometry2D.CreateSpriteQuad(
                particle.Position, particle.Rotation, particle.Size, particle.Color, opacity,
                simulationSpace == ParticleSimulationSpace.World,
                systemPosition, systemRotation, systemScale, sizeScale, visual, camera);
            sink.Submit(this, quad, BatchKey(visual));
        }

    }
    private static Color WithOpacity(Color color, Fix64 opacity) => new(color.r, color.g, color.b, color.a * opacity);
    private static Vector2 Abs(Vector2 value) => new(Fix64.Abs(value.x), Fix64.Abs(value.y));
}
