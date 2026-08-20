namespace BEngine.ExampleTests.DependencyInjectionArchitecture;

public sealed class InjectedLegacyRuntimeSystem(
    ScopedProbe dependency,
    LegacyRuntimeObservation observation) : ISceneRuntimeSystem
{
    public ScopedProbe Dependency { get; } = dependency;

    public void Start(Scene scene)
    {
        observation.DependencyId = Dependency.Id;
        observation.StartCount++;
    }
}
