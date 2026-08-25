namespace BEngine.ExampleTests.DependencyInjectionArchitecture;

public sealed class InjectedRuntimeSystem(
    ScopedProbe dependency,
    DisposableProbe disposable,
    RuntimeSystemObservation observation) : ISceneRuntimeSystem
{
    public ScopedProbe Dependency { get; } = dependency;

    public void Start(Scene scene)
    {
        observation.DependencyIds.Add(Dependency.Id);
        observation.Disposables.Add(disposable);
        observation.StartCount++;
    }
}
