namespace BEngine.Cinemachine;

[AddComponentMenu("Cinemachine/Impulse Source")]
public sealed class CinemachineImpulseSource : Behaviour
{
    private Fix64 _startedAt = -Fix64.One;
    private Vector2 _velocity;

    public Fix64 duration
    {
        get;
        set => field = Fix64.Max(Fix64.Parse("0.001"), value);
    } = Fix64.Parse("0.3");
    public Fix64 radius
    {
        get;
        set => field = Fix64.Max(Fix64.Zero, value);
    } = 10;

    public void GenerateImpulse() => GenerateImpulse(transform.right);

    public void GenerateImpulse(Vector2 velocity)
    {
        _velocity = velocity;
        _startedAt = Time.time;
    }

    internal Vector2 Evaluate(Vector2 listenerPosition)
    {
        if (_startedAt < Fix64.Zero || !enabled || !gameObject.activeInHierarchy) return Vector2.zero;
        var age = Time.time - _startedAt;
        if (age < Fix64.Zero || age >= duration) return Vector2.zero;
        var distance = Vector2.Distance(transform.position, listenerPosition);
        if (radius > Fix64.Zero && distance >= radius) return Vector2.zero;
        var spatial = radius <= Fix64.Epsilon ? Fix64.One : Fix64.One - distance / radius;
        var envelope = Fix64.One - age / duration;
        return _velocity * spatial * envelope * envelope;
    }
}
