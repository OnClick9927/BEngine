namespace BEngine.Physics2D;

public sealed class PhysicsMaterial2D : BObject
{
    public Fix64 friction { get; set; } = Fix64.Parse("0.6");
    public Fix64 bounciness { get; set; }
}
