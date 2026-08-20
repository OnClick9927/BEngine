namespace BEngine.ExampleTests.InspectorComponentActions;

internal sealed class ComponentClipboardProbe : MonoBehaviour
{
    public int[] numbers = [];
    public List<NestedValue> nestedValues = [];
    public BObject referencedObject = null!;

    public ComponentClipboardProbe() { }
}
