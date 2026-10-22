namespace BEngine.Physics2D;

[CreateAssetMenu(fileName = "New Physics Material 2D", menuName = "Physics 2D/Physics Material 2D", order = 320)]
public sealed class PhysicsMaterial2D : ScriptableObject
{
    public Fix64 friction
    {
        get;
        set => field = Fix64.Max(Fix64.Zero, value);
    } = Fix64.Parse("0.6");
    public Fix64 bounciness
    {
        get;
        set => field = Fix64.Clamp(value, Fix64.Zero, Fix64.One);
    }
}
