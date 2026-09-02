namespace BEngine.Profiling;

public readonly struct ProfilerMarker
{
    public string category { get; }
    public string name { get; }

    public ProfilerMarker(string name) : this("Scripts", name) { }

    public ProfilerMarker(string category, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        this.category = category.Trim();
        this.name = name.Trim();
    }

    public ProfilerScope Auto() => Profiler.BeginSample(category, name);
    public void Begin() => Profiler.BeginSample(category, name);
    public void End() => Profiler.EndSample();
}
