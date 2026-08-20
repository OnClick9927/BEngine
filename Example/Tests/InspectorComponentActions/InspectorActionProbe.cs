namespace BEngine.ExampleTests.InspectorComponentActions;

internal sealed class InspectorActionProbe : MonoBehaviour
{
    public int probeValue = 12;
    public string probeLabel = "Default";
    [NonSerialized] public int resetCalls;
    [NonSerialized] public int validateCalls;

    public InspectorActionProbe() { }

    public override void Reset()
    {
        resetCalls++;
        probeValue = 21;
        probeLabel = "Reset";
    }

    public override void OnValidate() => validateCalls++;
}
