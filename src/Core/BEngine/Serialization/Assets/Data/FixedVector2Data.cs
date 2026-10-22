namespace BEngine.Serialization;

internal sealed class FixedVector2Data
{
    public string X { get; set; } = "0";
    public string Y { get; set; } = "0";

    public FixedVector2Data() { }
    public FixedVector2Data(BEngine.Vector2 value)
    {
        X = value.x.ToString();
        Y = value.y.ToString();
    }
    public FixedVector2Data(BEngine.Fix64 x, BEngine.Fix64 y) : this(new BEngine.Vector2(x, y)) { }
    public BEngine.Vector2 ToVector2() => new(BEngine.Fix64.Parse(X), BEngine.Fix64.Parse(Y));
}
