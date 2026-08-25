namespace BEngine.ExampleTests.SceneRuntimeArchitecture;

internal sealed class SceneProbeBehaviour : MonoBehaviour
{
    public int Value;
    public int StartCount;
    public int UpdateCount;
    public int DestroyCount;
    public Action? AwakeProbe { get; set; }

    public override void Awake() => AwakeProbe?.Invoke();
    public override void Start() => StartCount++;
    public override void Update() => UpdateCount++;
    public override void OnDestroy() => DestroyCount++;
}
