namespace BEngine.Physics3D;

[DisallowMultipleComponent]
[AddComponentMenu("Physics/Rigidbody")]
public sealed class Rigidbody : Component
{
    private Vector3 _accumulatedAcceleration;

    public Fix64 mass { get; set; } = Fix64.One;
    public Fix64 linearDamping { get; set; }
    public Fix64 angularDamping { get; set; } = Fix64.Parse("0.05");
    public bool useGravity { get; set; } = true;
    public bool isKinematic { get; set; }
    public bool detectCollisions { get; set; } = true;
    public Vector3 linearVelocity { get; set; }
    public Vector3 angularVelocity { get; set; }
    public RigidbodyConstraints constraints { get; set; }
    public CollisionDetectionMode collisionDetectionMode { get; set; }
    public RigidbodyInterpolation interpolation { get; set; }
    public Vector3 position { get => transform.position; set => transform.position = value; }
    public Quaternion rotation { get => transform.rotation; set => transform.rotation = value; }
    public bool IsSleeping { get; private set; }

    public void AddForce(Vector3 force, ForceMode mode = ForceMode.Force)
    {
        if (isKinematic || mass <= Fix64.Zero) return;
        var acceleration = mode switch
        {
            ForceMode.Acceleration => force,
            ForceMode.Impulse => force / mass / Time.fixedDeltaTime,
            ForceMode.VelocityChange => force / Time.fixedDeltaTime,
            _ => force / mass
        };
        _accumulatedAcceleration += acceleration;
        WakeUp();
    }

    public void AddForceAtPosition(Vector3 force, Vector3 position, ForceMode mode = ForceMode.Force) =>
        AddForce(force, mode);
    public void MovePosition(Vector3 target) => position = target;
    public void MoveRotation(Quaternion target) => rotation = target;
    public void Sleep() { IsSleeping = true; linearVelocity = Vector3.zero; angularVelocity = Vector3.zero; }
    public void WakeUp() => IsSleeping = false;

    internal Vector3 ConsumeAcceleration()
    {
        var value = _accumulatedAcceleration;
        _accumulatedAcceleration = Vector3.zero;
        return value;
    }
}
