using System.Buffers;

namespace BEngine.Audio;

public sealed class AudioRuntimeSystem : ISceneRuntimeSystem
{
    private static readonly Comparison<AudioSource> PriorityOrder =
        static (left, right) => left.priority.CompareTo(right.priority);
    private readonly List<AudioSource> _sources = [];
    public string packageId => "com.bengine.audio";
    public int order => 75;

    public void Update(Scene scene, Fix64 deltaTime)
    {
        scene.GetComponents(_sources);
        if (_sources.Count == 0) return;
        var output = AudioOutput.current;
        if (output.sampleRate <= 0 || output.channelCount <= 0) return;
        var frameCount = Math.Clamp(Mathf.CeilToInt(Fix64.Max(Fix64.Zero, deltaTime) * output.sampleRate),
            0, output.sampleRate / 4);
        if (frameCount == 0) return;
        var length = checked(frameCount * output.channelCount);
        var buffer = ArrayPool<float>.Shared.Rent(length);
        try
        {
            var samples = buffer.AsSpan(0, length);
            samples.Clear();
            if (_sources.Count > 1) _sources.Sort(PriorityOrder);
            foreach (var source in _sources)
                if (source.enabled && source.gameObject.activeInHierarchy)
                    source.Mix(samples, output.sampleRate, output.channelCount, deltaTime);
            for (var index = 0; index < samples.Length; index++)
                samples[index] = Math.Clamp(samples[index], -1f, 1f);
            output.Submit(samples);
        }
        finally { ArrayPool<float>.Shared.Return(buffer, clearArray: false); }
    }
}
