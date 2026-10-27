namespace BEngine.Audio;

/// <summary>One decoder/cache per voice, never shared by independently playing AudioSources.</summary>
internal sealed class AudioVoiceReader : IDisposable
{
    static AudioVoiceReader()
    {
        // Finishing a voice is a separate cold branch from starting one.
        // Compile its measured fade body at first voice construction rather
        // than on a later scene's audio frame; do not create or mix a voice.
        if (!OperatingSystem.IsWindows() || !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ||
            !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeCompiled ||
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith("Mono", StringComparison.OrdinalIgnoreCase)) return;
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        foreach (var name in new[] { nameof(WriteFadeOut), "get_" + nameof(IsCurrent) })
        {
            var method = typeof(AudioVoiceReader).GetMethod(name, flags);
            if (method is not null) System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(method.MethodHandle);
        }
    }

    private readonly AudioSampleReader _reader;
    private readonly float[] _buffer;
    private int _start = -1, _frames;
    private readonly int _version;
    private float[] _decodedPcm;
    private readonly int _channels;
    private int _fadeIn;
    private bool _hasLoopOrigin;
    private float _loopLeft, _loopRight;
    private bool _hasLoopTail;
    private float _tailLeft, _tailRight;
    private readonly AudioClip? _clip;
    private readonly AudioPlaybackData? _data;
    private readonly int _samples;
    internal bool IsCurrent => _data is { } data ? data.IsValid : _version == _clip!.dataVersion;
    internal float LastLeft, LastRight;
    internal void ResumeAfterSkip() => _fadeIn = 192;
    internal void WriteFadeOut(Span<float> output, int channels)
    {
        var frames = Math.Min(192, output.Length / channels);
        for (var frame = 0; frame < frames; frame++)
        {
            var gain = 1 - (frame + 1) / (float)frames;
            if (channels == 1) output[frame] += (LastLeft + LastRight) * .5f * gain;
            else { output[frame * channels] += LastLeft * gain; output[frame * channels + 1] += LastRight * gain; }
        }
    }
    internal AudioVoiceReader(AudioClip clip, bool fadeIn = false)
    {
        _fadeIn = fadeIn ? 192 : 0;
        _decodedPcm = clip.DecodedPcmForVoice; _channels = clip.channels;
        _clip = clip; _samples = clip.samples; _version = clip.dataVersion; _reader = clip.OpenReader();
        try { _buffer = clip.HasDecodedPcm ? [] : new float[2048 * clip.channels]; }
        catch { _reader.Dispose(); throw; }
    }
    internal AudioVoiceReader(AudioPlaybackData data, bool fadeIn = false)
    {
        _data = data; _samples = data.Samples; _channels = data.Channels;
        _fadeIn = fadeIn ? 192 : 0; _decodedPcm = data.Pcm;
        _reader = data.OpenReader();
        try
        {
            if (_reader.samples != data.Samples || _reader.channels != data.Channels || _reader.frequency != data.Frequency)
                throw new InvalidDataException("Audio source format changed during independent playback.");
            _buffer = data.Pcm.Length > 0 ? [] : new float[2048 * data.Channels];
        }
        catch { _reader.Dispose(); throw; }
    }
    // Decoded PCM mixes in the Core kernel; streaming retains its bounded reader window.
    internal bool TryMixPcm(Span<float> output, int channels, Fix64 step, float leftGain, float rightGain,
        bool loop, ref Fix64 cursor, out bool ended)
    {
        ended = false;
        if (_decodedPcm.Length == 0) return false;
        ended = PcmSampleMixer.MixDecoded(_decodedPcm, _channels, output, channels, step,
            leftGain, rightGain, loop, ref cursor, ref _fadeIn, ref LastLeft, ref LastRight);
        return true;
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    internal bool TryStereo(Fix64 cursor, bool loop, out float left, out float right)
    {
        var frame = Mathf.FloorToInt(cursor); var fraction = (float)(cursor - frame);
        if (_decodedPcm.Length > 0)
        {
            var index = frame * _channels;
            left = _decodedPcm[index]; right = _channels > 1 ? _decodedPcm[index + 1] : left;
            if (fraction > 0)
            {
                var pcmNext = frame + 1 == _decodedPcm.Length / _channels ? (loop ? 0 : frame) : frame + 1;
                var at = pcmNext * _channels; var pcmNextLeft = _decodedPcm[at];
                left += (pcmNextLeft - left) * fraction;
                right += ((_channels > 1 ? _decodedPcm[at + 1] : pcmNextLeft) - right) * fraction;
            }
            Fade(ref left, ref right); return true;
        }
        if (!Frame(frame, out left, out right)) return false;
        if (fraction <= 0) { Fade(ref left, ref right); return true; }
        var next = frame + 1;
        if (next == _samples) next = loop ? 0 : frame;
        if (!Frame(next, out var nextLeft, out var nextRight)) return false;
        left += (nextLeft - left) * fraction; right += (nextRight - right) * fraction;
        Fade(ref left, ref right);
        return true;
    }
    private void Fade(ref float left, ref float right)
    { if (_fadeIn > 0) { var gain = (192 - --_fadeIn) / 192f; left *= gain; right *= gain; } }
    private bool Frame(int frame, out float left, out float right)
    {
        // The interpolation pair at the loop seam can be requested repeatedly
        // at low pitch. Keep both endpoints outside the sliding window so it
        // cannot alternate between decoding the track's end and beginning.
        if (frame == 0 && _hasLoopOrigin) { left = _loopLeft; right = _loopRight; return true; }
        if (frame == _samples - 1 && _hasLoopTail) { left = _tailLeft; right = _tailRight; return true; }
        left = right = 0;
        if (_start < 0 || frame < _start || frame >= _start + _frames)
        {
            // Interpolation asks for N+1 before it has finished using N. Keep
            // that shared boundary frame when advancing the decoder window;
            // otherwise sub-unit playback rates seek backwards every boundary.
            var overlap = _frames > 0 && frame == _start + _frames ? 1 : 0;
            if (overlap != 0)
                Array.Copy(_buffer, (_frames - 1) * _channels, _buffer, 0, _channels);
            var start = frame - overlap;
            if (_reader.position != frame) _reader.Seek(frame);
            var count = Math.Min(2048 - overlap, _samples - frame) * _channels;
            var read = 0;
            using var budget = LoopExecutionGuard.AllowIterations(count + 1);
            while (read < count)
            {
                var current = _reader.Read(_buffer, overlap * _channels + read, count - read);
                if (current == 0)
                {
                    if (!_reader.hasPendingSamples) throw new InvalidDataException("Audio stream ended before the clip's declared duration.");
                    if (read == 0) { _start = start; _frames = overlap; return false; }
                    break;
                }
                read += current;
            }
            _start = start; _frames = overlap + read / _channels;
        }
        var index = (frame - _start) * _channels;
        left = _buffer[index]; right = _channels > 1 ? _buffer[index + 1] : left;
        if (frame == 0) { _hasLoopOrigin = true; _loopLeft = left; _loopRight = right; }
        if (frame == _samples - 1) { _hasLoopTail = true; _tailLeft = left; _tailRight = right; }
        return true;
    }
    public void Dispose() { _decodedPcm = []; _reader.Dispose(); }
}

