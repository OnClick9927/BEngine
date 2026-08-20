using BEngine.Entities;

namespace BEngine;

internal sealed class LegacySceneRuntimeSystemAdapter(ISceneRuntimeSystem system) : ISystem
{
    public int order => system.order;

    public void OnStartRunning(ref SystemState state)
    {
        if (state.Scene is { } scene) system.Start(scene);
    }

    public void OnFixedUpdate(ref SystemState state)
    {
        if (state.Scene is { } scene) system.FixedUpdate(scene, state.DeltaTime);
    }

    public void OnUpdate(ref SystemState state)
    {
        if (state.Scene is { } scene) system.Update(scene, state.DeltaTime);
    }

    public void OnStopRunning(ref SystemState state)
    {
        if (state.Scene is { } scene) system.Stop(scene);
    }
}
