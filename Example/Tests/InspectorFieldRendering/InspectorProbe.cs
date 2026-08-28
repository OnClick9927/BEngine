using BEngine;

namespace BEngine.ExampleTests.InspectorFieldRendering;

internal sealed class InspectorProbe : ScriptableObject
{
    [Range(0, 10)] public float speed = 2;
    public Vector2 offset = new(Fix64.FromDecimal(11.25m), Fix64.FromDecimal(-22.5m));
    public Vector4 weights = new(Fix64.FromDecimal(1.234567m), Fix64.FromDecimal(-2.5m),
        Fix64.FromDecimal(3.75m), Fix64.FromDecimal(4.125m));
    public GameObject? gameObjectReference = null;
}

internal sealed class NestedInspectorComponent : MonoBehaviour
{
    public string description = "Nested component";
    public NestedInspectorData a = new() { age = 23 };
    public NestedInspectorData b = new() { age = 47 };
    public NestedInspectorData? nullable = null;
    public NestedInspectorData[] items = [new() { age = 31 }];
    public List<NestedInspectorData> entries = [new() { age = 37 }];
    public NestedInspectorData[]? optionalItems = null;
    public List<NestedInspectorData>? optionalEntries = null;
    public NestedInspectorCycle cycle = NestedInspectorCycle.CreateCycle();
    public NestedInspectorDepth deep = NestedInspectorDepth.Create(16);
    public NestedInspectorData throwing
    {
        get => throw new InvalidOperationException("Inspector getter fixture failed.");
        set { }
    }
    public int zAfterThrow = 97;
}

[Serializable]
internal sealed class NestedInspectorData
{
    public int age;
    [field: SerializeField] public int experience { get; set; } = 5;
    [HideInInspector] public int hidden = 99;
}

[Serializable]
internal sealed class NestedInspectorCycle
{
    public int value = 1;
    public NestedInspectorCycle? next;

    internal static NestedInspectorCycle CreateCycle()
    {
        var result = new NestedInspectorCycle();
        result.next = result;
        return result;
    }
}

[Serializable]
internal sealed class NestedInspectorDepth
{
    public NestedInspectorDepth? next;

    internal static NestedInspectorDepth Create(int levels)
    {
        var root = new NestedInspectorDepth();
        var current = root;
        for (var index = 1; index < levels; index++)
        {
            current.next = new NestedInspectorDepth();
            current = current.next;
        }
        return root;
    }
}
