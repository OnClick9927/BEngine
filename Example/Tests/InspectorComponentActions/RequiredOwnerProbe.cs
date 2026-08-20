namespace BEngine.ExampleTests.InspectorComponentActions;

[RequireComponent(typeof(RequiredMiddleProbe), typeof(RequiredSiblingProbe))]
internal sealed class RequiredOwnerProbe : MonoBehaviour
{
    public RequiredOwnerProbe() { }
}
