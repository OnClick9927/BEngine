namespace BEngine;

[AddComponentMenu("2D Animation/Bone 2D")]
public sealed class Bone2D : MonoBehaviour
{
    public Fix64 length
    {
        get;
        set => field = Fix64.Max(Fix64.Parse("0.001"), value);
    } = Fix64.One;

    public Color color { get; set; } = new(
        Fix64.Parse("0.25"), Fix64.Parse("0.85"), Fix64.One, Fix64.One);

    public Vector2 tip => transform.TransformPoint(new Vector2(length, 0));
}
