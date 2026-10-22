using Microsoft.Extensions.DependencyInjection;

namespace BEngine;

internal sealed class ScopedSceneRuntimeSystem(
    Type systemType,
    IServiceScope scope,
    ISceneRuntimeSystem system) : ISceneRuntimeSystem
{
    private IServiceScope? _scope = scope;

    internal Type SystemType { get; } = systemType;
    internal ISceneRuntimeSystem Instance => system;
    public string packageId => system.packageId;
    public int order => system.order;

    public void Start(Scene scene) => system.Start(scene);
    public void BeforeUpdate(Scene scene, Fix64 deltaTime) => system.BeforeUpdate(scene, deltaTime);
    public void FixedUpdate(Scene scene, Fix64 fixedDeltaTime) =>
        system.FixedUpdate(scene, fixedDeltaTime);
    public void Update(Scene scene, Fix64 deltaTime) => system.Update(scene, deltaTime);

    public void Stop(Scene scene)
    {
        try { system.Stop(scene); }
        finally { DisposeScope(); }
    }

    internal void DisposeScope()
    {
        var scope = _scope;
        _scope = null;
        scope?.Dispose();
    }
}
