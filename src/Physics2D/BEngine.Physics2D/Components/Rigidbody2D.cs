namespace BEngine.Physics2D;

[DisallowMultipleComponent]
[AddComponentMenu("Physics 2D/Rigidbody 2D")]
public sealed class Rigidbody2D : Component
{
    private Vector2 _accumulatedAcceleration;
    private Fix64 _accumulatedAngularAcceleration;

    public Fix64 mass { get; set; } = Fix64.One;
    public Fix64 gravityScale { get; set; } = Fix64.One;
    public Fix64 linearDamping { get; set; }
    public Fix64 angularDamping { get; set; } = Fix64.Parse("0.05");
    public RigidbodyType2D bodyType { get; set; } = RigidbodyType2D.Dynamic;
    public bool simulated { get; set; } = true;
    public Vector2 linearVelocity { get; set; }
    public Fix64 angularVelocity { get; set; }
    public RigidbodyConstraints2D constraints { get; set; }
    public CollisionDetectionMode collisionDetectionMode { get; set; }
    public RigidbodyInterpolation interpolation { get; set; }
    public Vector2 position { get => transform.position; set => transform.position = value; }
    public Fix64 rotation { get => transform.rotation; set => transform.rotation = value; }
    public bool IsSleeping { get; private set; }

    public void AddForce(Vector2 force, ForceMode mode = ForceMode.Force)
    {
        if (bodyType != RigidbodyType2D.Dynamic || mass <= Fix64.Zero) return;
        _accumulatedAcceleration += mode switch
        {
            ForceMode.Acceleration => force,
            ForceMode.Impulse => force / mass / Time.fixedDeltaTime,
            ForceMode.VelocityChange => force / Time.fixedDeltaTime,
            _ => force / mass
        };
        WakeUp();
    }

    public void AddTorque(Fix64 torque, ForceMode mode = ForceMode.Force)
    {
        if (bodyType != RigidbodyType2D.Dynamic || mass <= Fix64.Zero) return;
        _accumulatedAngularAcceleration += mode switch
        {
            ForceMode.Acceleration => torque,
            ForceMode.Impulse => torque / mass / Time.fixedDeltaTime,
            ForceMode.VelocityChange => torque / Time.fixedDeltaTime,
            _ => torque / mass
        };
        WakeUp();
    }

    public void AddForceAtPosition(Vector2 force, Vector2 worldPosition, ForceMode mode = ForceMode.Force)
    {
        AddForce(force, mode);
        var arm = worldPosition - position;
        AddTorque(arm.x * force.y - arm.y * force.x, mode);
    }

    public void MovePosition(Vector2 target) => position = target;
    public void MoveRotation(Fix64 target) => rotation = target;
    public void Sleep() { IsSleeping = true; linearVelocity = Vector2.zero; angularVelocity = Fix64.Zero; }
    public void WakeUp() => IsSleeping = false;

    internal Vector2 ConsumeAcceleration()
    {
        var value = _accumulatedAcceleration;
        _accumulatedAcceleration = Vector2.zero;
        return value;
    }

    internal Fix64 ConsumeAngularAcceleration()
    {
        var value = _accumulatedAngularAcceleration;
        _accumulatedAngularAcceleration = Fix64.Zero;
        return value;
    }
}
