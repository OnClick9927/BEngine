namespace BEngine.Physics2D;

public sealed partial class Rigidbody2D
{
    private MassData2D? _stepMass;
    private readonly List<Collider2D> _massColliders = [];
    private Vector2 _manualCenter;
    public bool useAutoMass { get; set; }
    public bool automaticCenterOfMass { get; set; } = true;
    public Vector2 centerOfMass { get => MassProperties.LocalCenter; set => _manualCenter = value; }
    /// <summary>Moment about the selected center in mass * world-units squared.</summary>
    public Fix64 inertia => MassProperties.Inertia;
    public Vector2 worldCenterOfMass => transform.TransformPoint(MassProperties.LocalCenter);
    public Vector2 GetPointVelocity(Vector2 worldPoint)
    {
        var arm = worldPoint - worldCenterOfMass;
        return linearVelocity + new Vector2(-arm.y, arm.x) * (angularVelocity * Mathf.Deg2Rad);
    }
    public void ResetCenterOfMass() { automaticCenterOfMass = true; _stepMass = null; }
    internal Vector2 Constrain(Vector2 value) => new(
        constraints.HasFlag(RigidbodyConstraints2D.FreezePositionX) ? Fix64.Zero : value.x,
        constraints.HasFlag(RigidbodyConstraints2D.FreezePositionY) ? Fix64.Zero : value.y);
    internal void BeginMassStep() => _stepMass = CalculateMassProperties();
    internal void EndMassStep() => _stepMass = null;
    internal MassData2D MassProperties => _stepMass ?? CalculateMassProperties();
    private MassData2D CalculateMassProperties()
    {
        var total = new ShapeMass2D();
        gameObject.GetComponentsInChildren(_massColliders);
        try
        {
            foreach (var collider in _massColliders)
            {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger || collider.density <= 0 ||
                    !ReferenceEquals(collider.attachedRigidbody, this) || collider.compositeOwner is { enabled: true }) continue;
                var data = ShapeMass2D.Measure(PhysicsShapeFactory2D.Create(collider), position, collider.density);
                total = ShapeMass2D.Combine(total, data);
            }
            var selectedMass = useAutoMass ? Fix64.Max((Fix64).0001, total.Mass) : mass;
            if (total.Mass <= Fix64.Epsilon)
                return new(selectedMass, selectedMass, automaticCenterOfMass ? Vector2.zero : _manualCenter);
            var worldOffset = automaticCenterOfMass ? total.Center : transform.TransformVector(_manualCenter);
            var selectedInertia = (total.Inertia / total.Mass + (total.Center - worldOffset).sqrMagnitude) * selectedMass;
            return new(selectedMass, Fix64.Max((Fix64).0001, selectedInertia), transform.InverseTransformVector(worldOffset));
        }
        finally { _massColliders.Clear(); }
    }
}

internal readonly record struct MassData2D(Fix64 Mass, Fix64 Inertia, Vector2 LocalCenter);

internal readonly record struct ShapeMass2D(Fix64 Mass, Fix64 Inertia, Vector2 Center)
{
    internal static ShapeMass2D Combine(ShapeMass2D a, ShapeMass2D b)
    {
        if (a.Mass <= 0) return b;
        if (b.Mass <= 0) return a;
        var mass = a.Mass + b.Mass;
        var center = a.Center + (b.Center - a.Center) * (b.Mass / mass);
        return new(mass, a.Inertia + b.Inertia + (a.Center - b.Center).sqrMagnitude * (a.Mass * (b.Mass / mass)), center);
    }
    internal static ShapeMass2D Measure(PhysicsShape2D shape, Vector2 origin, Fix64 density)
    {
        if (shape.Type == PhysicsShapeType2D.Compound)
        {
            var result = new ShapeMass2D();
            foreach (var part in shape.Parts) result = Combine(result, Measure(part, origin, density));
            return result;
        }
        if (shape.Type == PhysicsShapeType2D.Circle)
        {
            var mass = Fix64.Pi * shape.Radius * shape.Radius * density;
            return new(mass, mass * shape.Radius * shape.Radius / 2, shape.Center - origin);
        }
        var reference = shape.Vertices[0];
        var twiceArea = Fix64.Zero; var weightedCenter = Vector2.zero; var moment = Fix64.Zero;
        for (var index = 0; index < shape.Vertices.Length; index++)
        {
            var a = shape.Vertices[index] - reference; var b = shape.Vertices[(index + 1) % shape.Vertices.Length] - reference;
            var cross = a.x * b.y - a.y * b.x;
            twiceArea += cross; weightedCenter += (a + b) * cross;
            moment += cross * (a.sqrMagnitude + Vector2.Dot(a, b) + b.sqrMagnitude);
        }
        if (twiceArea <= Fix64.Epsilon) return default;
        var area = twiceArea / 2; var centerOffset = weightedCenter / (3 * twiceArea);
        return new(area * density, Fix64.Max(0, moment / 12 - area * centerOffset.sqrMagnitude) * density,
            reference - origin + centerOffset);
    }
}
