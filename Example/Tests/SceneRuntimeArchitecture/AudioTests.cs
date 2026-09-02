using System.Buffers.Binary;
using BEngine.Audio;

namespace BEngine.ExampleTests.SceneRuntimeArchitecture;

internal static class AudioTests
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineAudio_{Guid.NewGuid():N}");
        var resourceRoot = Path.Combine(root, "Resources");
        Directory.CreateDirectory(resourceRoot);
        var path = Path.Combine(resourceRoot, "tone.wav");
        File.WriteAllBytes(path, CreateWave(sampleRate: 8000, frames: 800));
        Resources.RegisterResourceRoot(root);
        try
        {
            var clip = Resources.Load<AudioClip>("tone") ??
                       throw new InvalidOperationException("Resources did not decode the registered AudioClip.");
            Require(clip.channels == 1 && clip.frequency == 8000 && clip.samples == 800 &&
                    clip.length == Fix64.Parse("0.1") && clip.loadState == AudioDataLoadState.Loaded,
                "WAV metadata was decoded incorrectly.");
            var data = new float[clip.samples];
            Require(clip.GetData(data, 0) && data.Any(static value => Math.Abs(value) > 0.1f),
                "AudioClip did not expose decoded PCM samples.");

            using var scene = new Scene("Audio test");
            var source = scene.CreateGameObject("Source").AddComponent<AudioSource>();
            source.clip = clip;
            source.playOnAwake = false;
            source.Play();
            var output = new CaptureOutput();
            var previous = AudioOutput.current;
            AudioOutput.current = output;
            try
            {
                new AudioRuntimeSystem().Update(scene, Fix64.Parse("0.05"));
                Require(output.SubmitCount == 1 && output.LastSamples.Length == 800 &&
                        output.LastSamples.Any(static value => Math.Abs(value) > 0.05f) && source.isPlaying,
                    "Audio runtime did not mix an active 2D source into stereo output.");
                new AudioRuntimeSystem().Update(scene, Fix64.Parse("0.06"));
                Require(!source.isPlaying, "A non-looping AudioSource did not stop at the end of its clip.");

                source.loop = true;
                source.Play();
                new AudioRuntimeSystem().Update(scene, Fix64.Parse("0.2"));
                Require(source.isPlaying, "A looping AudioSource stopped at the end of its clip.");
            }
            finally { AudioOutput.current = previous; }
        }
        finally
        {
            Resources.UnregisterResourceRoot(root);
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] CreateWave(int sampleRate, int frames)
    {
        var dataLength = checked(frames * sizeof(short));
        var bytes = new byte[44 + dataLength];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 36 + dataLength);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), sampleRate * sizeof(short));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32), sizeof(short));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34), 16);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), dataLength);
        for (var frame = 0; frame < frames; frame++)
        {
            var sample = (short)(Math.Sin(frame * Math.PI * 2 * 440 / sampleRate) * short.MaxValue * 0.5);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(44 + frame * sizeof(short)), sample);
        }
        return bytes;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CaptureOutput : IAudioOutput
    {
        public int sampleRate => 8000;
        public int channelCount => 2;
        public int SubmitCount { get; private set; }
        public float[] LastSamples { get; private set; } = [];
        public void Submit(ReadOnlySpan<float> interleavedSamples)
        {
            SubmitCount++;
            LastSamples = interleavedSamples.ToArray();
        }
    }
}
