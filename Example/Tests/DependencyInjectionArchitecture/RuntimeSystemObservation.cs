namespace BEngine.ExampleTests.DependencyInjectionArchitecture;

public sealed class RuntimeSystemObservation
{
    public List<Guid> DependencyIds { get; } = [];
    public List<DisposableProbe> Disposables { get; } = [];
    public int StartCount { get; set; }
}
