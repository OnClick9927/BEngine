namespace BEngine.Audio;

internal sealed class AudioDspProcessor(AudioMixerEffectType type, int channels) : IDisposable
{
    internal AudioMixerEffectType Type { get; } = type;
    private readonly double[] _z1 = new double[channels], _z2 = new double[channels];
    private float[] _delay = [];
    private int _delayPosition;
    private float[] _convolution = [];
    private int _convolutionHash;
    private double _delayEnergy, _envelope, _phase;
    private IAudioDspPlugin? _plugin;
    private string _pluginId = string.Empty;
    internal bool HasTail => _plugin?.HasTail == true || _delayEnergy > 1e-16 || _z1.Any(value => Math.Abs(value) > 1e-8) || _z2.Any(value => Math.Abs(value) > 1e-8);
    public void Dispose() { _plugin?.Dispose(); _plugin = null; }

    internal void Process(Span<float> samples, int sampleRate, AudioMixerEffect settings)
    {
        using var budget = LoopExecutionGuard.AllowIterations(samples.Length + 1);
        switch (Type)
        {
            case AudioMixerEffectType.Gain:
                var gain = (float)Math.Pow(10, (double)settings.gainDb / 20);
                for (var i = 0; i < samples.Length; i++) samples[i] *= gain;
                break;
            case AudioMixerEffectType.LowPass:
            case AudioMixerEffectType.HighPass:
                Filter(samples, sampleRate, settings); break;
            case AudioMixerEffectType.Delay:
                Delay(samples, sampleRate, settings); break;
            case AudioMixerEffectType.Compressor:
                Compress(samples, sampleRate, settings); break;
            case AudioMixerEffectType.ParametricEqualizer:
                ParametricEqualizer(samples, sampleRate, settings); break;
            case AudioMixerEffectType.Distortion:
                Distortion(samples, settings); break;
            case AudioMixerEffectType.Chorus:
                Chorus(samples, sampleRate, settings); break;
            case AudioMixerEffectType.ConvolutionReverb:
                Convolution(samples, sampleRate, settings); break;
            case AudioMixerEffectType.Plugin:
                if (!string.Equals(_pluginId, settings.pluginId, StringComparison.Ordinal)) { Dispose(); _pluginId = settings.pluginId; }
                if (!string.IsNullOrWhiteSpace(_pluginId))
                {
                    _plugin ??= AudioDspPlugins.Create(_pluginId, sampleRate, channels);
                    _plugin.Process(samples, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(settings.pluginParameters));
                }
                break;
        }
        for (var i = 0; i < samples.Length; i++)
            if (!float.IsFinite(samples[i])) samples[i] = 0;
    }

    private void Filter(Span<float> samples, int sampleRate, AudioMixerEffect settings)
    {
        // RBJ biquad coefficients: https://www.w3.org/TR/audio-eq-cookbook/
        var omega = 2 * Math.PI * Math.Min((double)settings.cutoffHz, sampleRate * .49) / sampleRate;
        var cosine = Math.Cos(omega); var alpha = Math.Sin(omega) / (2 * (double)settings.resonance);
        var a0 = 1 + alpha; var a1 = -2 * cosine / a0; var a2 = (1 - alpha) / a0;
        var b0 = (Type == AudioMixerEffectType.LowPass ? 1 - cosine : 1 + cosine) / (2 * a0);
        var b1 = (Type == AudioMixerEffectType.LowPass ? 2 : -2) * b0;
        for (var i = 0; i < samples.Length; i++)
        {
            var channel = i % channels; var input = samples[i]; var output = b0 * input + _z1[channel];
            _z1[channel] = b1 * input - a1 * output + _z2[channel]; _z2[channel] = b0 * input - a2 * output;
            if (Math.Abs(_z1[channel]) < 1e-20) _z1[channel] = 0;
            if (Math.Abs(_z2[channel]) < 1e-20) _z2[channel] = 0;
            samples[i] = (float)output;
        }
    }
    private void Delay(Span<float> samples, int sampleRate, AudioMixerEffect settings)
    {
        var length = Math.Max(1, (int)Math.Round((double)settings.delaySeconds * sampleRate)) * channels;
        if (_delay.Length != length) { _delay = new float[length]; _delayPosition = 0; _delayEnergy = 0; }
        var wet = (float)settings.wet; var feedback = (float)settings.feedback;
        for (var i = 0; i < samples.Length; i++)
        {
            var past = _delay[_delayPosition]; var input = samples[i]; var next = input + past * feedback;
            if (Math.Abs(next) < 1e-9) next = 0;
            _delayEnergy += (double)next * next - (double)past * past;
            _delay[_delayPosition] = next; _delayPosition = (_delayPosition + 1) % _delay.Length;
            samples[i] = input * (1 - wet) + past * wet;
        }
        _delayEnergy = Math.Max(0, _delayEnergy);
    }
    private void Compress(Span<float> samples, int sampleRate, AudioMixerEffect settings)
    {
        var attack = settings.attackSeconds == 0 ? 0 : Math.Exp(-1 / ((double)settings.attackSeconds * sampleRate));
        var release = settings.releaseSeconds == 0 ? 0 : Math.Exp(-1 / ((double)settings.releaseSeconds * sampleRate));
        for (var i = 0; i < samples.Length; i += channels)
        {
            double peak = 0;
            for (var channel = 0; channel < channels; channel++) peak = Math.Max(peak, Math.Abs(samples[i + channel]));
            var coefficient = peak > _envelope ? attack : release;
            _envelope = peak + coefficient * (_envelope - peak);
            var above = Math.Max(0, 20 * Math.Log10(Math.Max(1e-12, _envelope)) - (double)settings.thresholdDb);
            var gain = (float)Math.Pow(10, ((double)settings.gainDb - above * (1 - 1 / (double)settings.ratio)) / 20);
            for (var channel = 0; channel < channels; channel++) samples[i + channel] *= gain;
        }
    }

    private void ParametricEqualizer(Span<float> samples, int sampleRate, AudioMixerEffect settings)
    {
        var omega = 2 * Math.PI * Math.Min((double)settings.cutoffHz, sampleRate * .49) / sampleRate;
        var cosine = Math.Cos(omega);
        var alpha = Math.Sin(omega) / (2 * (double)settings.resonance);
        var amplitude = Math.Pow(10, (double)settings.gainDb / 40);
        var a0 = 1 + alpha / amplitude;
        var b0 = (1 + alpha * amplitude) / a0;
        var b1 = -2 * cosine / a0;
        var b2 = (1 - alpha * amplitude) / a0;
        var a1 = -2 * cosine / a0;
        var a2 = (1 - alpha / amplitude) / a0;
        for (var i = 0; i < samples.Length; i++)
        {
            var channel = i % channels;
            var input = samples[i];
            var output = b0 * input + _z1[channel];
            _z1[channel] = b1 * input - a1 * output + _z2[channel];
            _z2[channel] = b2 * input - a2 * output;
            samples[i] = (float)output;
        }
    }

    private static void Distortion(Span<float> samples, AudioMixerEffect settings)
    {
        var drive = (double)settings.drive;
        var normalization = Math.Tanh(drive);
        var wet = (double)settings.wet;
        for (var i = 0; i < samples.Length; i++)
        {
            var input = samples[i];
            var shaped = Math.Tanh(input * drive) / normalization;
            samples[i] = (float)(input * (1 - wet) + shaped * wet);
        }
    }

    private void Chorus(Span<float> samples, int sampleRate, AudioMixerEffect settings)
    {
        var maximumDelay = Math.Max(2, (int)Math.Ceiling(((double)settings.delaySeconds +
            (double)settings.modulationDepthSeconds) * sampleRate) + 2);
        var length = maximumDelay * channels;
        if (_delay.Length != length) { _delay = new float[length]; _delayPosition = 0; _delayEnergy = 0; }
        var wet = (double)settings.wet;
        var feedback = (double)settings.feedback;
        for (var frame = 0; frame < samples.Length / channels; frame++)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var phase = _phase + channel * Math.PI * .5;
                var delayFrames = Math.Max(1, ((double)settings.delaySeconds +
                    Math.Sin(phase) * (double)settings.modulationDepthSeconds) * sampleRate);
                var read = _delayPosition / channels - delayFrames;
                while (read < 0) read += maximumDelay;
                var first = (int)Math.Floor(read) % maximumDelay;
                var second = (first + 1) % maximumDelay;
                var amount = read - Math.Floor(read);
                var past = _delay[first * channels + channel] * (1 - amount) +
                           _delay[second * channels + channel] * amount;
                var index = frame * channels + channel;
                var input = samples[index];
                var next = input + past * feedback;
                var write = _delayPosition + channel;
                var old = _delay[write];
                _delay[write] = (float)next;
                _delayEnergy += next * next - old * old;
                samples[index] = (float)(input * (1 - wet) + past * wet);
            }
            _delayPosition = (_delayPosition + channels) % _delay.Length;
            _phase += 2 * Math.PI * (double)settings.modulationRateHz / sampleRate;
            if (_phase >= 2 * Math.PI) _phase -= 2 * Math.PI;
        }
        _delayEnergy = Math.Max(0, _delayEnergy);
    }

    private void Convolution(Span<float> samples, int sampleRate, AudioMixerEffect settings)
    {
        var hash = new HashCode();
        hash.Add(sampleRate); hash.Add(settings.impulseResponseSampleRate);
        foreach (var tap in settings.impulseResponse) hash.Add(tap);
        var nextHash = hash.ToHashCode();
        if (_convolutionHash != nextHash)
        {
            _convolutionHash = nextHash;
            _convolution = ResampleImpulse(settings.impulseResponse, settings.impulseResponseSampleRate, sampleRate);
            _delay = new float[Math.Max(channels, _convolution.Length * channels)];
            _delayPosition = 0; _delayEnergy = 0;
        }
        if (_convolution.Length == 0) return;
        var wet = (double)settings.wet;
        var ringFrames = _convolution.Length;
        for (var frame = 0; frame < samples.Length / channels; frame++)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var index = frame * channels + channel;
                var input = samples[index];
                var old = _delay[_delayPosition + channel];
                _delay[_delayPosition + channel] = input;
                _delayEnergy += (double)input * input - (double)old * old;
                double output = 0;
                var history = _delayPosition / channels;
                for (var tap = 0; tap < _convolution.Length; tap++)
                {
                    var sourceFrame = history - tap;
                    if (sourceFrame < 0) sourceFrame += ringFrames;
                    output += _delay[sourceFrame * channels + channel] * _convolution[tap];
                }
                samples[index] = (float)(input * (1 - wet) + output * wet);
            }
            _delayPosition = (_delayPosition + channels) % _delay.Length;
        }
        _delayEnergy = Math.Max(0, _delayEnergy);
    }

    private static float[] ResampleImpulse(IReadOnlyList<float> source, int sourceRate, int outputRate)
    {
        if (source.Count == 0) return [];
        var length = Math.Clamp((int)Math.Round(source.Count * outputRate / (double)sourceRate), 1, 4096);
        var result = new float[length];
        for (var index = 0; index < length; index++)
        {
            var position = index * sourceRate / (double)outputRate;
            var first = Math.Min(source.Count - 1, (int)Math.Floor(position));
            var second = Math.Min(source.Count - 1, first + 1);
            var amount = position - first;
            result[index] = (float)(source[first] * (1 - amount) + source[second] * amount);
        }
        return result;
    }
}
