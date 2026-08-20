using BEngine.Entities;

namespace BEngine.ExampleTests.EcsRuntimeArchitecture;

internal struct PositionData : IComponentData
{
    public int X;
    public int Y;
}
