using BEngine;

namespace BEngine.ExampleTests.InspectorFieldRendering;

internal sealed class InspectorProbe : ScriptableObject
{
    [Range(0, 10)] public float speed = 2;
    public Vector2 offset = new(Fix64.FromDecimal(11.25m), Fix64.FromDecimal(-22.5m));
    public Vector4 weights = new(Fix64.FromDecimal(1.234567m), Fix64.FromDecimal(-2.5m),
        Fix64.FromDecimal(3.75m), Fix64.FromDecimal(4.125m));
}
