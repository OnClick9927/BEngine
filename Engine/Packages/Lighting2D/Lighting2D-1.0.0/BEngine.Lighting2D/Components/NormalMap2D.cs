namespace BEngine;

[AddComponentMenu("Rendering/2D Normal Map")]
[DisallowMultipleComponent]
public sealed class NormalMap2D : MonoBehaviour
{
    public Texture? texture { get; set; }
    public Fix64 strength { get; set; } = 1;
    public Vector2 uvScale { get; set; } = Vector2.one;
    public Vector2 uvOffset { get; set; }
    public override void OnValidate()
    {
        if (strength < 0) throw new InvalidOperationException("Normal map strength must be nonnegative.");
    }
}
