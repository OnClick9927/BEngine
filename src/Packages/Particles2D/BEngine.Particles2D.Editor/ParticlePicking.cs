using BEngine.Rendering;
namespace BEngine.Editor;

internal static class ParticlePicking
{
    internal static long Collect(Scene scene, RenderCamera camera, Vector2 viewportPoint, int viewportWidth, int viewportHeight, ICollection<ScenePickCandidate> candidates, long submissionOrder, Predicate<GameObject>? objectFilter)
    {
        var hierarchy = HierarchyOrder2D.Build(scene);
            foreach (var system in scene.QueryComponents<ParticleSystem2D>())
            {
                if (!system.enabled || !system.gameObject.activeInHierarchy || !camera.ContainsLayer(system.sortingLayer) || (objectFilter is not null && !objectFilter(system.gameObject))) continue;
                var hierarchyOrder = hierarchy.GetValueOrDefault(system.gameObject);
                var visual = system.ResolveSpriteUnchecked();
                foreach (var particle in system.particles)
                {
                    var world = system.simulationSpace == ParticleSimulationSpace.World;
                    var worldPosition = world ? particle.Position : system.transform.TransformPoint(particle.Position);
                    var color = new Color(particle.Color.r, particle.Color.g, particle.Color.b,
                        particle.Color.a * system.opacity);
                    var rotation = (world ? Fix64.Zero : system.transform.rotation) + particle.Rotation;
                    var sizeScale = world ? Vector2.one : system.transform.lossyScale;
                    var offset = new Vector2(
                        (Fix64.Half - visual.Pivot.x) * particle.Size.x,
                        (Fix64.Half - visual.Pivot.y) * particle.Size.y);
                    worldPosition += Transform.RotateVector(
                        Vector2.Scale(offset, sizeScale), rotation);
                    var renderedSize = Abs(Vector2.Scale(particle.Size, sizeScale));
                    if (!camera.IsVisible(worldPosition, rotation, renderedSize,
                            viewportWidth, viewportHeight)) continue;
                    var key = new RenderSortKey2D(system.sortingLayer, system.orderInLayer,
                        hierarchyOrder, system.TransparencyUnchecked(color), submissionOrder++);
                    if (color.a <= Fix64.Zero ||
                        !ScenePickingUtility.ContainsQuad(viewportPoint, worldPosition, rotation, renderedSize,
                            camera, viewportWidth, viewportHeight)) continue;
                    candidates.Add(new ScenePickCandidate(system.gameObject, key));
                }
            }

        return submissionOrder;
    }
    private static Vector2 Abs(Vector2 value) => new(Fix64.Abs(value.x), Fix64.Abs(value.y));
}
