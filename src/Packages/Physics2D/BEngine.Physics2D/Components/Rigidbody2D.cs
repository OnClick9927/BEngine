namespace BEngine.Physics2D;

[DisallowMultipleComponent]
[AddComponentMenu("Physics 2D/Rigidbody 2D")]
public sealed partial class Rigidbody2D : Component
{
    private Vector2 _accumulatedAcceleration;
    private Fix64 _accumulatedAngularAcceleration;

    public Fix64 mass
    {
        get => useAutoMass ? MassProperties.Mass : field;
        set => field = Fix64.Max(Fix64.Parse("0.0001"), value);
    } = Fix64.One;
    public Fix64 gravityScale { get; set; } = Fix64.One;
    public Fix64 linearDamping { get; set; }
    public Fix64 angularDamping { get; set; } = Fix64.Parse("0.05");
    public RigidbodyType2D bodyType { get; set; } = RigidbodyType2D.Dynamic;
    public bool simulated { get; set; } = true;
    public Vector2 linearVelocity { get; set; }
    public Vector2 velocity { get => linearVelocity; set => linearVelocity = value; }
    public Fix64 angularVelocity { get; set; }
    public RigidbodyConstraints2D constraints { get; set; }
    public CollisionDetectionMode collisionDetectionMode { get; set; }
    public RigidbodyInterpolation interpolation { get; set; }
    public Vector2 position { get => transform.position; set => transform.position = value; }
    public Fix64 rotation { get => transform.rotation; set => transform.rotation = value; }
    public bool IsSleeping { get; private set; }
    public bool isKinematic { get => bodyType == RigidbodyType2D.Kinematic; set => bodyType = value ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic; }

    public void AddForce(Vector2 force, ForceMode mode = ForceMode.Force)
    {
        if (!simulated || bodyType != RigidbodyType2D.Dynamic) return;
        if (mode is ForceMode.Impulse or ForceMode.VelocityChange)
            linearVelocity += Constrain(mode == ForceMode.Impulse ? force / mass : force);
        else _accumulatedAcceleration += mode switch
        {
            ForceMode.Acceleration => force,
            _ => force / mass
        };
        WakeUp();
    }

    public void AddTorque(Fix64 torque, ForceMode mode = ForceMode.Force)
    {
        if (!simulated || bodyType != RigidbodyType2D.Dynamic || constraints.HasFlag(RigidbodyConstraints2D.FreezeRotation)) return;
        if (mode is ForceMode.Impulse or ForceMode.VelocityChange)
            angularVelocity += mode == ForceMode.Impulse ? torque / inertia * Mathf.Rad2Deg : torque;
        else _accumulatedAngularAcceleration += mode switch
        {
            ForceMode.Acceleration => torque,
            _ => torque / inertia * Mathf.Rad2Deg
        };
        WakeUp();
    }

    public void AddForceAtPosition(Vector2 force, Vector2 worldPosition, ForceMode mode = ForceMode.Force)
    {
        AddForce(force, mode);
        var arm = worldPosition - worldCenterOfMass;
        var torque = arm.x * force.y - arm.y * force.x;
        // Acceleration/velocity modes describe a linear change independent of mass.
        AddTorque(mode is ForceMode.Acceleration or ForceMode.VelocityChange ? torque * mass : torque,
            mode is ForceMode.Acceleration ? ForceMode.Force : mode is ForceMode.VelocityChange ? ForceMode.Impulse : mode);
    }

    public void AddRelativeForce(Vector2 relativeForce, ForceMode mode = ForceMode.Force) =>
        AddForce(transform.TransformDirection(relativeForce), mode);

    public Vector2 GetPoint(Vector2 point) => transform.TransformPoint(point);
    public Vector2 GetRelativePoint(Vector2 point) => transform.InverseTransformPoint(point);
    public Vector2 GetVector(Vector2 vector) => transform.TransformVector(vector);
    public Vector2 GetRelativeVector(Vector2 vector) => transform.InverseTransformVector(vector);

    public void MovePosition(Vector2 target) => position = target;
    public void MoveRotation(Fix64 target) => rotation = target;
    public void Sleep() { IsSleeping = true; linearVelocity = Vector2.zero; angularVelocity = Fix64.Zero; }
    public void WakeUp() => IsSleeping = false;
    public bool IsAwake() => !IsSleeping;

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
