namespace BEngine;

[AddComponentMenu("Rendering/2D Lighting Environment")]
[DisallowMultipleComponent]
public sealed class LightingEnvironment2D : Behaviour
{
    public Color ambientColor { get; set; } = new(
        Fix64.Parse("0.08"), Fix64.Parse("0.08"), Fix64.Parse("0.08"), Fix64.One);
    public Fix64 ambientIntensity
    {
        get;
        set => field = Fix64.Max(Fix64.Zero, value);
    } = Fix64.One;
}
