using System;
using BEngine;

namespace BEngine.Audio;

/// <summary>Bounded stereo PCM queue with linked peak limiting and smooth discontinuity recovery.</summary>
public sealed class BufferedStereoPcmStream
{
    public const int Capacity = 5760;
    private const int FadeFrames = 192, Lookahead = 72, Window = Lookahead + 1;
    public const int TailFrames = FadeFrames + Lookahead;
    private readonly float[] _ring, _delay = new float[Window * 2], _peaks = new float[Window];
    private readonly long[] _indices = new long[Window];
    private int _read, _peakHead, _peakCount, _fade = FadeFrames, _attackRemaining;
    private long _frame;
    private float _gain = 1, _lastLeft, _lastRight, _joinLeft, _joinRight;
    private bool _join = true;
    public int Count { get; private set; }
    public int QueueResets { get; private set; }
    public int DroppedFrames => (int)Math.Min(int.MaxValue, TotalDroppedFrames);
    public long TotalDroppedFrames { get; private set; }
    public long UnderrunFrames { get; private set; }
    public long WrittenFrames { get; private set; }
    public long ConsumedFrames { get; private set; }
    public int CapacityFrames { get; }
    public int NonFiniteSamples { get; private set; }
    public float PeakPcm { get; private set; }
    public BufferedStereoPcmStream(int capacityFrames = Capacity)
    {
        if (capacityFrames is < 1 or > 96000) throw new ArgumentOutOfRangeException(nameof(capacityFrames));
        CapacityFrames = capacityFrames; _ring = new float[capacityFrames * 2];
    }

    /// <summary>Writes the accepted prefix without discarding queued audio; the caller retains the suffix.</summary>
    public bool TryWrite(ReadOnlySpan<float> samples, int available, out int acceptedFrames)
    {
        if (samples.Length % 2 != 0) throw new ArgumentException("Stereo PCM requires complete frames.", nameof(samples));
        acceptedFrames = Math.Min(samples.Length / 2, Math.Max(0, Math.Clamp(available, 0, CapacityFrames) - Count));
        using var budget = LoopExecutionGuard.AllowIterations(acceptedFrames * 2 + 1);
        for (var index = 0; index < acceptedFrames * 2; index++)
        {
            var value = samples[index]; if (!float.IsFinite(value)) { value = 0; NonFiniteSamples++; }
            _ring[(_read + Count * 2 + index) % _ring.Length] = value;
        }
        Count += acceptedFrames; WrittenFrames += acceptedFrames;
        return acceptedFrames == samples.Length / 2;
    }

    public void Write(ReadOnlySpan<float> samples, int available)
    {
        if (samples.Length % 2 != 0) throw new ArgumentException("Stereo PCM requires complete frames.", nameof(samples));
        available = Math.Clamp(available, 0, CapacityFrames);
        int total = samples.Length / 2, frames = Math.Min(total, available);
        if (frames == 0) { TotalDroppedFrames += total; if (total > 0) { _join = true; QueueResets++; } return; }
        int drop = Math.Max(0, Count + frames - available);
        if (drop > 0) { _read = (_read + drop * 2) % _ring.Length; Count -= drop; }
        if (drop > 0 || total > frames) { _join = true; QueueResets++; TotalDroppedFrames += drop + total - frames; }
        int start = (total - frames) * 2;
        using var budget = LoopExecutionGuard.AllowIterations(frames * 2 + 1);
        for (int i = 0; i < frames * 2; i++)
        {
            float value = samples[start+i];
            if (!float.IsFinite(value)) { value = 0; NonFiniteSamples++; }
            _ring[(_read + Count * 2 + i) % _ring.Length] = value;
        }
        Count += frames; WrittenFrames += frames;
    }

    public void Read(Span<short> pcm)
    {
        if (pcm.Length % 2 != 0) throw new ArgumentException("Stereo PCM requires complete frames.", nameof(pcm));
        using var budget = LoopExecutionGuard.AllowIterations(pcm.Length * 5 + 1);
        int frames = pcm.Length / 2;
        if (_join) { _fade = 0; _joinLeft = _lastLeft; _joinRight = _lastRight; _join = false; }
        int realFrames = Math.Min(frames, Count);
        ConsumedFrames += realFrames; UnderrunFrames += frames - realFrames;
        float tailLeft = 0, tailRight = 0;
        for (int i = 0; i < frames; i++, _frame++)
        {
            float left, right;
            if (i < realFrames)
            {
                left = _ring[(_read+i*2)%_ring.Length]; right = _ring[(_read+i*2+1)%_ring.Length];
                if (_fade < FadeFrames)
                {
                    float t = ++_fade / (float)FadeFrames; t = t*t*(3-2*t);
                    left = _joinLeft*(1-t)+left*t; right = _joinRight*(1-t)+right*t;
                }
                tailLeft = left; tailRight = right;
            }
            else
            {
                if (i == realFrames) { tailLeft = _lastLeft; tailRight = _lastRight; }
                float t = Math.Min(1, (i-realFrames+1)/(float)FadeFrames); t = 1-t*t*(3-2*t);
                left = tailLeft*t; right = tailRight*t; _join = true;
            }
            _lastLeft = left; _lastRight = right;
            int slot = (int)(_frame%Window)*2;
            _delay[slot] = left; _delay[slot+1] = right;
            float peak = Math.Max(Math.Abs(left),Math.Abs(right));
            while (_peakCount > 0 && _indices[_peakHead] < _frame-Lookahead) { _peakHead=(_peakHead+1)%Window; _peakCount--; }
            while (_peakCount > 0 && _peaks[(_peakHead+_peakCount-1)%Window] <= peak) _peakCount--;
            int index = (_peakHead+_peakCount)%Window; _peaks[index]=peak; _indices[index]=_frame; _peakCount++;
            float target = Math.Min(1,.86f/Math.Max(.0001f,_peaks[_peakHead]));
            if (target < _gain)
            {
                // Ramp down over the lookahead rather than abruptly scaling the
                // already-playing music when a loud weapon or voice begins.
                if (_attackRemaining == 0) _attackRemaining = Lookahead;
                _gain += (target-_gain)/_attackRemaining; _attackRemaining--;
            }
            else
            {
                _attackRemaining=0;
                _gain=Math.Min(target,1-(1-_gain)*.9997685f);
            }
            int delayed = (int)((_frame+1)%Window)*2;
            float delayedPeak=Math.Max(Math.Abs(_delay[delayed]),Math.Abs(_delay[delayed+1]));
            _gain=Math.Min(_gain,.86f/Math.Max(.0001f,delayedPeak));
            float outLeft = _frame < Lookahead ? 0 : _delay[delayed]*_gain;
            float outRight = _frame < Lookahead ? 0 : _delay[delayed+1]*_gain;
            PeakPcm=Math.Max(PeakPcm,Math.Max(Math.Abs(outLeft),Math.Abs(outRight)));
            pcm[i*2]=(short)(Math.Clamp(outLeft,-.86f,.86f)*32767);
            pcm[i*2+1]=(short)(Math.Clamp(outRight,-.86f,.86f)*32767);
        }
        _read=(_read+realFrames*2)%_ring.Length; Count-=realFrames;
    }
}
