namespace BEngine;

/// <summary>Allocation-free mixing of decoded, interleaved PCM into an existing output buffer.</summary>
public static class PcmSampleMixer
{
    /// <summary>
    /// Adds one voice to the output without clearing or clipping existing samples. Mono input is
    /// duplicated; multichannel input uses its first two channels. Output channels beyond stereo
    /// are left untouched. The cursor and playback step use source frames in Q32.32 units.
    /// </summary>
    /// <returns>Whether a non-looping voice reached the end of its decoded source.</returns>
    /// <remarks>
    /// The source must contain complete frames and the output may end with an incomplete frame,
    /// which is left untouched. A fade counter of 192 applies the existing 192-frame resume ramp.
    /// This method owns no resources, performs no decoding and does not change voice selection.
    /// </remarks>
    public static bool MixDecoded(ReadOnlySpan<float> pcm, int sourceChannels, Span<float> output,
        int outputChannels, Fix64 step, float leftGain, float rightGain, bool loop,
        ref Fix64 cursor, ref int fadeIn, ref float lastLeft, ref float lastRight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceChannels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(outputChannels, 1);
        if (pcm.IsEmpty || pcm.Length % sourceChannels != 0)
            throw new ArgumentException("Decoded PCM must contain complete source frames.", nameof(pcm));
        var samples = pcm.Length / sourceChannels;
        var outputFrames = output.Length / outputChannels;
        using var budget = LoopExecutionGuard.AllowIterations(outputFrames + 1);

        // Retain the integer-rate block path and its original state/rounding order.
        if (fadeIn == 0 && step == Fix64.One && cursor.RawValue % Fix64.One.RawValue == 0)
        {
            var frame = Mathf.FloorToInt(cursor); var written = 0;
            while (written < outputFrames)
            {
                if (frame >= samples) { if (!loop) break; frame %= samples; }
                var count = Math.Min(samples - frame, outputFrames - written);
                for (var i = 0; i < count; i++)
                {
                    var index = (frame + i) * sourceChannels;
                    var left = pcm[index] * leftGain;
                    var right = (sourceChannels > 1 ? pcm[index + 1] : pcm[index]) * rightGain;
                    if (outputChannels == 1) output[written + i] += (left + right) * .5f;
                    else { var at = (written + i) * outputChannels; output[at] += left; output[at + 1] += right; }
                    lastLeft = left; lastRight = right;
                }
                written += count; frame += count;
            }
            if (loop) frame %= samples;
            else frame = Math.Min(frame, samples);
            cursor = frame;
            return !loop && frame >= samples;
        }

        for (var frame = 0; frame < outputFrames; frame++)
        {
            var sourceFrame = Mathf.FloorToInt(cursor);
            if (sourceFrame >= samples)
            {
                if (!loop) break;
                cursor %= samples;
                sourceFrame = Mathf.FloorToInt(cursor);
            }
            // Keep the exact TryStereo interpolation, fade and gain operation order.
            var fraction = (float)(cursor - sourceFrame);
            var index = sourceFrame * sourceChannels;
            var left = pcm[index]; var right = sourceChannels > 1 ? pcm[index + 1] : left;
            if (fraction > 0)
            {
                var next = sourceFrame + 1 == samples ? (loop ? 0 : sourceFrame) : sourceFrame + 1;
                var at = next * sourceChannels; var nextLeft = pcm[at];
                left += (nextLeft - left) * fraction;
                right += ((sourceChannels > 1 ? pcm[at + 1] : nextLeft) - right) * fraction;
            }
            if (fadeIn > 0)
            {
                var gain = (192 - --fadeIn) / 192f;
                left *= gain; right *= gain;
            }
            if (outputChannels == 1) output[frame] += (left * leftGain + right * rightGain) * .5f;
            else
            {
                output[frame * outputChannels] += left * leftGain;
                output[frame * outputChannels + 1] += right * rightGain;
            }
            cursor += step;
            lastLeft = left * leftGain; lastRight = right * rightGain;
        }
        if (loop) cursor %= samples;
        else if (cursor >= samples) { cursor = samples; return true; }
        return false;
    }
}
