namespace BEngine.Physics3D;

public abstract class Collider : Component
{
    public bool isTrigger { get; set; }
    public Vector3 center { get; set; } = Vector3.zero;
    public PhysicMaterial? material { get; set; }
    public Rigidbody? attachedRigidbody => gameObject.GetComponent<Rigidbody>();
    public Bounds bounds => PhysicsWorld.GetBounds(this);
    public Vector3 ClosestPoint(Vector3 position) => PhysicsWorld.GetClosestPoint(this, position);
}

public interface ITerrainCollisionSource
{
    Vector3 boundsMin { get; }
    Vector3 boundsMax { get; }
    bool SampleSurface(Vector3 worldPosition, out Fix64 height, out Vector3 normal);
}

[AddComponentMenu("Physics/Box Collider")]
public sealed class BoxCollider : Collider
{
    public Vector3 size { get; set; } = Vector3.one;
}

[AddComponentMenu("Physics/Sphere Collider")]
public sealed class SphereCollider : Collider
{
    public Fix64 radius { get; set; } = Fix64.Half;
}

[AddComponentMenu("Physics/Capsule Collider")]
public sealed class CapsuleCollider : Collider
{
    public Fix64 radius { get; set; } = Fix64.Half;
    public Fix64 height { get; set; } = 2;
    public int direction { get; set; } = 1;
}

[AddComponentMenu("Physics/Mesh Collider")]
public sealed class MeshCollider : Collider
{
    public string sharedMesh { get; set; } = "Cube";
    public bool convex { get; set; }
    public Vector3 boundsSize { get; set; } = Vector3.one;
}
