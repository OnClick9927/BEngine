using BEngine;

namespace BEngine.ExampleTests.EcsRuntimeArchitecture;

internal sealed class EcsProbeBehaviour : MonoBehaviour
{
    public int Value;
    public int StartCount;
    public int UpdateCount;
    public int DestroyCount;

    public override void Start() => StartCount++;
    public override void Update() => UpdateCount++;
    public override void OnDestroy() => DestroyCount++;
}
