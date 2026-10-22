using System.Buffers;

namespace BEngine.Audio;

public sealed class AudioRuntimeSystem : ISceneRuntimeSystem, IRuntimeFrameSystem
{
    private readonly List<AudioSource> _sources = [];
    private readonly List<AudioSource> _frameSources = [];
    private readonly List<AudioSource> _realSources = [];
    private readonly HashSet<AudioSource> _mixedSources = new(ReferenceEqualityComparer.Instance);
    private readonly FrameDataBuffer<AudioSource.AudioVoiceCandidate> _voices = new();
    private readonly List<AudioMixerGroup> _routeGroups = [];
    private readonly AudioMixerRouting.ValidationBatch _routeValidation = new();
    private readonly PriorityQueue<AudioSource.AudioVoiceCandidate, (int, Fix64, int, int, int)> _selected = new();
    private bool _frameSelection;
    private bool _independentFrame;
    private bool _frameActive;
    public AudioVoiceSnapshot voiceSnapshot { get; private set; }
    public long skippedOutputFrames { get; private set; }
    private readonly Dictionary<Scene, AudioMixerGraph> _graphs = new(ReferenceEqualityComparer.Instance);
    public string packageId => "com.bengine.audio";
    public int order => 75;
    public string frameGroupId => "com.bengine.audio.output";
    public void BeginFrame(IReadOnlyList<Scene> scenes, Fix64 deltaTime)
    {
        if (_frameActive) throw new InvalidOperationException("An audio runtime frame is already active.");
        _frameSources.Clear();
        // Frame snapshots are arrays; direct package callers may use Lists.
        // Keep other implementations' enumeration and disposal semantics.
        if (scenes is Scene[] array)
            foreach (var scene in array) AppendSceneSources(scene);
        else if (scenes is List<Scene> list && list.GetType() == typeof(List<Scene>))
            foreach (var scene in list) AppendSceneSources(scene);
        else
            foreach (var scene in scenes) AppendSceneSources(scene);
        _independentFrame = AudioIndependentClock.Synchronize(_frameSources);
        if (_independentFrame)
        {
            var logical = 0; var real = 0;
            foreach (var source in _frameSources) { logical += source.activeVoiceCount; real += source.IndependentRealVoices; }
            voiceSnapshot = new(logical, real, Math.Max(0, logical - real));
            AudioRuntimeSettings.voiceSnapshot = voiceSnapshot;
            _frameActive = true; return;
        }
        SelectVoices(_frameSources); _frameSelection = true; AudioOutput.BeginMixFrame();
        _frameActive = true;
    }
    private void AppendSceneSources(Scene scene)
    {
        scene.GetComponents(_sources);
        _frameSources.AddRange(_sources);
    }
    public void EndFrame(IReadOnlyList<Scene> scenes, Fix64 deltaTime)
    { try { if (!_independentFrame) AudioOutput.EndMixFrame(); } finally { _frameActive = false; _independentFrame = false; _frameSelection = false; _frameSources.Clear(); } }

    private void SelectVoices(IReadOnlyList<AudioSource> sources)
    {
        _voices.Clear(); _selected.Clear(); var logical = 0;
        for (var i = 0; i < sources.Count; i++) { var source = sources[i]; source.GatherVoices(_voices); logical += source.activeVoiceCount; }
        var maximum = AudioRuntimeSettings.maximumRealVoices;
        if (maximum == 0) foreach (ref readonly var voice in _voices.WrittenSpan) voice.Source.SelectVoice(voice.Voice);
        else
        {
            foreach (ref readonly var voice in _voices.WrittenSpan)
            {
                if (_selected.Count < maximum) _selected.Enqueue(voice, voice.Rank);
                else if (_selected.TryPeek(out _, out var worst) && voice.Rank.CompareTo(worst) > 0)
                { _selected.Dequeue(); _selected.Enqueue(voice, voice.Rank); }
            }
            foreach (var entry in _selected.UnorderedItems) entry.Element.Source.SelectVoice(entry.Element.Voice);
        }
        var real = maximum == 0 ? _voices.Count : _selected.Count;
        voiceSnapshot = new(logical, real, logical - real);
        AudioRuntimeSettings.voiceSnapshot = voiceSnapshot;
        _voices.Clear(); _selected.Clear();
    }

    public void Update(Scene scene, Fix64 deltaTime)
    {
        if (_independentFrame) return;
        // Audio uses device time, independent of gameplay pause/slow motion.
        // Direct package callers still supply their own explicit sample clock.
        if (Time.IsFrameActive) deltaTime = Time.unscaledDeltaTime;
        scene.GetComponents(_sources);
        if (!_frameSelection) SelectVoices(_sources);
        _graphs.TryGetValue(scene, out var graph);
        if (_sources.Count == 0 && graph?.HasTail != true) { if (graph is not null) { graph.Dispose(); _graphs.Remove(scene); } return; }
        var output = AudioOutput.current;
        if (output.sampleRate is < 8000 or > 384000 || output.channelCount is < 1 or > 8) return;
        if (AudioListener.pause) return;
        if (graph is null) { graph = new(); _graphs.Add(scene, graph); }
        var frameCount = graph.AllocateFrames(deltaTime, output.sampleRate);
        if (frameCount == 0) return;
        if (output is IAudioBufferedOutput buffered)
        {
            buffered.SetExpectedSubmissionFrames(frameCount);
            var skipped = Math.Max(0, frameCount - buffered.availableFrames);
            if (skipped > 0)
            {
                skippedOutputFrames += skipped;
                foreach (var source in _sources) source.Mix(Span<float>.Empty, output.sampleRate, output.channelCount,
                    (Fix64)skipped / output.sampleRate, applyMixerGain: false, useVoiceSelection: true, virtualFrames: skipped,
                    retainSkippedDecoder: true);
                frameCount -= skipped;
                deltaTime = Fix64.Max(0, deltaTime - (Fix64)skipped / output.sampleRate);
            }
        }
        if (frameCount == 0) return;
        var length = checked(frameCount * output.channelCount);
        var buffer = ArrayPool<float>.Shared.Rent(length);
        try
        {
            var samples = buffer.AsSpan(0, length);
            samples.Clear();
            // Validate all authored routes even when some voices are virtual.
            try
            {
                _routeGroups.Clear();
                foreach (var source in _sources)
                    if (source.enabled && source.gameObject.activeInHierarchy && source.outputAudioMixerGroup is { } group) _routeGroups.Add(group);
                _routeValidation.Validate(_routeGroups); graph.Error = null;
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
            {
                if (graph.Error != error.Message) Debug.LogError("Audio mixer routing failed: " + error.Message);
                graph.Error = error.Message; AudioOutput.SubmitMix(samples, output); return;
            }
            _realSources.Clear();
            _mixedSources.Clear();
            foreach (var source in _sources)
                if (!source.enabled || !source.gameObject.activeInHierarchy) source.Stop();
                else if (source.HasRealVoice || source.HasRetiringVoice) { _realSources.Add(source); _mixedSources.Add(source); }
            for (var offset = 0; offset < frameCount; offset += 1024)
            {
                var count = Math.Min(1024, frameCount - offset);
                var chunk = samples.Slice(offset * output.channelCount, count * output.channelCount);
                var chunkTime = deltaTime * count / frameCount;
                try
                {
                    graph.Prepare(_realSources, output.sampleRate, output.channelCount, chunk.Length);
                    foreach (var source in _realSources)
                        source.Mix(source.outputAudioMixerGroup is null ? chunk : graph.BufferFor(source),
                            output.sampleRate, output.channelCount, chunkTime, applyMixerGain: false, useVoiceSelection: true);
                    graph.Process(chunk);
                }
                catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
                {
                    if (graph.Error != error.Message) Debug.LogError("Audio mixer processing failed: " + error.Message);
                    graph.Error = error.Message; samples.Clear(); AudioOutput.SubmitMix(samples, output); return;
                }
                finally { graph.FinishFrame(); }
            }
            foreach (var source in _sources)
                if (source.enabled && source.gameObject.activeInHierarchy && !_mixedSources.Contains(source))
                    source.Mix(Span<float>.Empty, output.sampleRate, output.channelCount, deltaTime,
                        applyMixerGain: false, useVoiceSelection: true, virtualFrames: frameCount);
            AudioOutput.SubmitMix(samples, output);
        }
        finally { _routeGroups.Clear(); graph.FinishFrame(); ArrayPool<float>.Shared.Return(buffer, clearArray: false); }
    }

    public string? GetMixerError(Scene scene) => _graphs.GetValueOrDefault(scene)?.Error;
    public void Stop(Scene scene)
    {
        AudioIndependentClock.Shutdown();
        scene.GetComponents(_sources); foreach (var source in _sources) source.Stop();
        _sources.Clear();
        _voices.Clear(); _selected.Clear(); _frameSources.Clear(); _realSources.Clear();
        _mixedSources.Clear();
        _routeGroups.Clear();
        if (_graphs.Remove(scene, out var graph)) graph.Dispose();
    }
}
