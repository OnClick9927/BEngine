namespace BEngine.Documents;

public sealed class FixedVector2Document : Document
{
    public string X { get; set; } = "0";
    public string Y { get; set; } = "0";

    public FixedVector2Document() { }
    public FixedVector2Document(BEngine.Vector2 value)
    {
        X = value.x.ToString();
        Y = value.y.ToString();
    }
    public FixedVector2Document(BEngine.Fix64 x, BEngine.Fix64 y) : this(new BEngine.Vector2(x, y)) { }
    public BEngine.Vector2 ToVector2() => new(BEngine.Fix64.Parse(X), BEngine.Fix64.Parse(Y));
}
