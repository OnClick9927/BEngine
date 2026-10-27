namespace BEngine.Audio;

public sealed partial class AudioSource
{
    /// <summary>
    /// Prepares clips, their primary/one-shot voice reader paths and this source's
    /// mixer route using small, discarded PCM blocks. No sound is submitted and
    /// this source's playback state, cursor and voices remain unchanged. The
    /// source must stay enabled in an active scene until preparation completes.
    /// </summary>
    /// <remarks>
    /// Each voice uses at most 128 output frames per cooperative step. Readers,
    /// effect buffers and loading-budget ownership are released on cancellation,
    /// failure or completion. Streaming clips retain their authored load policy;
    /// preparing them does not decompress the full track or retain extra readers.
    /// </remarks>
    public async BTask PreparePlaybackAsync(IEnumerable<AudioClip> clips, AsyncLoadContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(clips);
        context ??= new AsyncLoadContext();
        var token = context.cancellationToken;
        token.ThrowIfCancellationRequested();
        var unique = new HashSet<AudioClip>(ReferenceEqualityComparer.Instance);
        foreach (var clip in clips)
        {
            token.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(clip);
            if (unique.Count >= 1024 && !unique.Contains(clip))
                throw new InvalidOperationException("An audio preparation supports at most 1024 distinct clips.");
            unique.Add(clip);
        }
        if (unique.Count == 0) return;
        var pending = unique.ToArray();
        using var loading = FrameWorkBudget.EnterLoading(token);
        var output = AudioOutput.current;
        var rate = output.sampleRate; var channels = output.channelCount;
        if (rate is < 8000 or > 384000 || channels is < 1 or > 8)
            throw new InvalidOperationException("Audio preparation requires a supported output format.");
        var route = outputAudioMixerGroup; var controller = mixerController;
        var sources = new[] { this };
        using var graph = new AudioMixerGraph();
        var scratch = new float[128 * channels];
        AudioVoiceReader? reader = null;
        var cursor = Fix64.Zero; var playing = true; var wasVirtual = false;
        var index = 0; var pass = 0;
        try
        {
            await context.RunSteps(() =>
            {
                ObjectDisposedException.ThrowIf(isDestroyed, this);
                if (!enabled || !gameObject.activeInHierarchy)
                    throw new InvalidOperationException("The preparing AudioSource must remain active and enabled.");
                if (!ReferenceEquals(route, outputAudioMixerGroup) || !ReferenceEquals(controller, mixerController))
                    throw new InvalidOperationException("The source's mixer route changed during preparation.");
                var clip = pending[index];
                ObjectDisposedException.ThrowIf(clip.isDestroyed, clip);
                if (!clip.LoadAudioData()) throw new InvalidDataException($"Audio preparation failed for '{clip.name}': {clip.loadError}");
                if (clip.loadState == AudioDataLoadState.Loading)
                { clip.PrioritizeLoading(); return CooperativeWorkStatus.Blocked; }
                if (clip.loadState != AudioDataLoadState.Loaded)
                    throw new InvalidDataException($"Audio preparation failed for '{clip.name}': {clip.loadError}");
                scratch.AsSpan().Clear();
                try
                {
                    if (route is not null) graph.Prepare(sources, rate, channels, scratch.Length);
                    var target = route is null ? scratch.AsSpan() : graph.BufferFor(this);
                    // Both primary playback and one-shots use this exact voice
                    // path. Local ownership exercises both starts without Play,
                    // temporary GameObjects or a fixed number of game frames.
                    MixVoice(target, rate, channels, clip, Fix64.One, Fix64.Zero,
                        ref cursor, false, ref playing, ref reader, ref wasVirtual, throwOnFailure: true);
                    if (route is not null) graph.Process(scratch);
                }
                finally { graph.FinishFrame(); }
                if (playing && cursor == Fix64.Zero) return CooperativeWorkStatus.Blocked;
                reader?.Dispose(); reader = null;
                cursor = Fix64.Zero; playing = true; wasVirtual = false;
                if (++pass == 2) { pass = 0; index++; }
                context.ReportProgress((index * 2f + pass) / (pending.Length * 2));
                return index == pending.Length ? CooperativeWorkStatus.Complete : CooperativeWorkStatus.Continue;
            });
        }
        finally { reader?.Dispose(); }
    }
}
