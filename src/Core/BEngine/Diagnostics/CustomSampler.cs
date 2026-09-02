namespace BEngine.Profiling;

public sealed class CustomSampler
{
    private readonly ProfilerMarker _marker;
    public string name => _marker.name;

    private CustomSampler(string name) => _marker = new ProfilerMarker(name);

    public static CustomSampler Create(string name) => new(name);
    public void Begin() => _marker.Begin();
    public void End() => _marker.End();
}
