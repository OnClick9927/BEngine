namespace BEngine.Testing;

public sealed class TestContext
{
    private static readonly AsyncLocal<TestContext?> CurrentSlot = new();
    private readonly SceneRuntime? _runtime;

    internal TestContext(TestMode mode, CancellationToken cancellationToken, Scene? scene, SceneRuntime? runtime)
    {
        Mode = mode;
        CancellationToken = cancellationToken;
        Scene = scene;
        _runtime = runtime;
    }

    public static TestContext Current => CurrentSlot.Value ??
        throw new InvalidOperationException("No BEngine test is currently executing.");
    public TestMode Mode { get; }
    public CancellationToken CancellationToken { get; }
    public Scene? Scene { get; }

    public void Tick(int frames = 1, Fix64? deltaTime = null)
    {
        if (_runtime is null)
            throw new InvalidOperationException("Tick is only available to Play tests.");
        if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
        var delta = deltaTime ?? (Fix64)(1m / 60m);
        for (var frame = 0; frame < frames; frame++)
        {
            CancellationToken.ThrowIfCancellationRequested();
            _runtime.Tick(delta);
        }
    }

    internal static IDisposable Push(TestContext context)
    {
        var previous = CurrentSlot.Value;
        CurrentSlot.Value = context;
        return new Restore(previous);
    }

    private sealed class Restore(TestContext? previous) : IDisposable
    {
        public void Dispose() => CurrentSlot.Value = previous;
    }
}
