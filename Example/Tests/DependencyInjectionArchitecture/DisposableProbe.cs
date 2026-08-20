namespace BEngine.ExampleTests.DependencyInjectionArchitecture;

public sealed class DisposableProbe : IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}
