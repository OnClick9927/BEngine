using BEngine;

namespace BEngine.ExampleTests.EditorFeatureFaultIsolation;

internal sealed class FaultProbeAsset : ScriptableObject
{
    [FaultingProperty] public int brokenValue = 17;
    public int healthyValue = 23;
}
