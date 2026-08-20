namespace BEngine.Entities;

internal readonly struct WorldContextScope : IDisposable
{
    private readonly World? _previous;

    internal WorldContextScope(World world)
    {
        _previous = World.SetCurrent(world);
    }

    public void Dispose() => World.SetCurrent(_previous);
}
