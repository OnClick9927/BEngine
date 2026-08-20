using BEngine.Entities;

namespace BEngine.ExampleTests.DependencyInjectionArchitecture;

public sealed class InjectedEcsSystem(ScopedProbe dependency) : SystemBase
{
    public ScopedProbe Dependency { get; } = dependency;
    public int CreateCount { get; private set; }

    public override void OnCreate(ref SystemState state)
    {
        base.OnCreate(ref state);
        CreateCount++;
    }

    public override void OnUpdate(ref SystemState state) { }
}
