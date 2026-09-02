using BEngine.Profiling;

namespace BEngine.ExampleTests.SceneRuntimeArchitecture;

internal static class ProfilerTests
{
    internal static void Run()
    {
        var samples = new List<ProfilerSample>();
        var counters = new List<(string Category, string Name, double Value)>();
        void OnSample(ProfilerSample sample) => samples.Add(sample);
        void OnCounter(string category, string name, double value) => counters.Add((category, name, value));
        Profiler.sampleCompleted += OnSample;
        Profiler.counterUpdated += OnCounter;
        Profiler.enabled = true;
        try
        {
            var marker = new ProfilerMarker("Gameplay", "Move Player");
            using (marker.Auto())
            using (Profiler.BeginSample("Gameplay", "Resolve Collision"))
                _ = new byte[32];
            var counter = new ProfilerCounterValue<int>("Gameplay", "Visible Actors");
            counter.value = 17;

            Require(samples.Count == 2 &&
                    samples[0].name == "Resolve Collision" && samples[0].parentToken != 0 &&
                    samples[1].name == "Move Player" && samples[1].parentToken == 0 &&
                    samples.All(static sample => sample.elapsedMilliseconds >= 0 && sample.threadId > 0),
                "Runtime profiler did not preserve nested marker details.");
            Require(counters.SequenceEqual([("Gameplay", "Visible Actors", 17d)]),
                "Runtime profiler counter did not publish its value.");
            Require(Profiler.GetTotalAllocatedMemoryLong() >= Profiler.GetMonoHeapSizeLong() &&
                    Profiler.usedHeapSizeLong >= 0,
                "Runtime profiler memory counters returned invalid values.");
        }
        finally
        {
            Profiler.enabled = false;
            Profiler.sampleCompleted -= OnSample;
            Profiler.counterUpdated -= OnCounter;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
