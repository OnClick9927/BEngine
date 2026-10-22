namespace BEngine.Editor;

internal static class ParticleGizmos
{
    [DrawGizmo(GizmoType.Selected)]
    private static void DrawParticleDirection(ParticleSystem2D particles, GizmoType state)
    {
        var direction = particles.startDirection.sqrMagnitude > Fix64.Epsilon
            ? particles.startDirection.normalized
            : Vector2.up;
        var travel = direction * particles.startSpeed * Fix64.Max(Fix64.Zero, particles.startLifetime);
        var origin = particles.transform.position;
        Gizmos.color = Color.white;
        Gizmos.DrawArrow(origin, origin + particles.transform.TransformVector(travel));
    }

}
