using BEngine.Entities;

namespace BEngine.ExampleTests.EcsRuntimeArchitecture;

internal sealed class EcsProbeSystem : ISystem
{
    public List<string> Calls { get; } = [];
    public World? ObservedWorld { get; private set; }
    public int UpdatedEntities { get; private set; }

    public void OnCreate(ref SystemState state)
    {
        ObservedWorld = state.World;
        Calls.Add("Create");
    }

    public void OnStartRunning(ref SystemState state) => Calls.Add("Start");

    public void OnFixedUpdate(ref SystemState state)
    {
        if (!state.IsFixedStep) throw new InvalidOperationException("Fixed update state was not marked fixed-step.");
        Calls.Add("FixedUpdate");
    }

    public void OnUpdate(ref SystemState state)
    {
        if (state.IsFixedStep) throw new InvalidOperationException("Frame update state was marked fixed-step.");
        var query = state.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<PositionData>());
        foreach (var entity in query)
        {
            ref var position = ref state.EntityManager.GetComponentDataRW<PositionData>(entity);
            position.X++;
            if (!state.EntityManager.HasComponent<VelocityData>(entity))
                state.CommandBuffer.AddComponentData(entity, new VelocityData { X = 1, Y = 0 });
            UpdatedEntities++;
        }
        Calls.Add("Update");
    }

    public void OnStopRunning(ref SystemState state) => Calls.Add("Stop");
    public void OnDestroy(ref SystemState state) => Calls.Add("Destroy");
}
