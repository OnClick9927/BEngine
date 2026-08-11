namespace BEngine.Physics3D;

public enum ForceMode { Force, Acceleration, Impulse, VelocityChange }
public enum CollisionDetectionMode { Discrete, Continuous, ContinuousDynamic, ContinuousSpeculative }
public enum RigidbodyInterpolation { None, Interpolate, Extrapolate }
public enum QueryTriggerInteraction { UseGlobal, Ignore, Collide }

[Flags]
public enum RigidbodyConstraints
{
    None = 0,
    FreezePositionX = 2,
    FreezePositionY = 4,
    FreezePositionZ = 8,
    FreezeRotationX = 16,
    FreezeRotationY = 32,
    FreezeRotationZ = 64,
    FreezePosition = FreezePositionX | FreezePositionY | FreezePositionZ,
    FreezeRotation = FreezeRotationX | FreezeRotationY | FreezeRotationZ,
    FreezeAll = FreezePosition | FreezeRotation
}

public sealed class PhysicMaterial : BObject
{
    public Fix64 dynamicFriction { get; set; } = Fix64.Parse("0.6");
    public Fix64 staticFriction { get; set; } = Fix64.Parse("0.6");
    public Fix64 bounciness { get; set; }
}

public readonly struct ContactPoint
{
    public Vector3 point { get; init; }
    public Vector3 normal { get; init; }
    public Fix64 separation { get; init; }
    public Collider thisCollider { get; init; }
    public Collider otherCollider { get; init; }
}

public sealed class Collision
{
    public Collider collider { get; internal init; } = null!;
    public GameObject gameObject => collider.gameObject;
    public Rigidbody? rigidbody => collider.attachedRigidbody;
    public Vector3 relativeVelocity { get; internal init; }
    public IReadOnlyList<ContactPoint> contacts { get; internal init; } = [];
    public int contactCount => contacts.Count;
    public Transform transform => collider.transform;
    public ContactPoint GetContact(int index) => contacts[index];
}

public readonly struct RaycastHit
{
    public Collider collider { get; init; }
    public Vector3 point { get; init; }
    public Vector3 normal { get; init; }
    public Fix64 distance { get; init; }
    public Rigidbody? rigidbody => collider?.attachedRigidbody;
    public Transform? transform => collider?.transform;
}
