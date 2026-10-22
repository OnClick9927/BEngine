namespace BEngine.Cinemachine;

[AddComponentMenu("Cinemachine/Impulse Listener")]
public sealed class CinemachineImpulseListener : Behaviour
{
    public Fix64 gain
    {
        get;
        set => field = Fix64.Max(Fix64.Zero, value);
    } = Fix64.One;
    public LayerMask layerMask { get; set; } = new(ulong.MaxValue);

    internal Vector2 Evaluate(Scene scene)
    {
        var result = Vector2.zero;
        var sources = scene.QueryComponents<CinemachineImpulseSource>();
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            if (!layerMask.Contains(source.gameObject.layer)) continue;
            result += source.Evaluate(transform.position);
        }
        return result * gain;
    }
}
