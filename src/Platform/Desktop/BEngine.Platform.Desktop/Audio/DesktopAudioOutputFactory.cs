namespace BEngine.Platform;

/// <summary>Desktop device selection; opening a device remains lazy.</summary>
public sealed class DesktopAudioOutputFactory : IPlatformAudioOutputFactory
{
    public static DesktopAudioOutputFactory Shared { get; } = new();

    public IPlatformAudioOutput? CreateDefault(int sampleRate, int channelCount) =>
        CreateDefault(new PlatformAudioOutputOptions(sampleRate, channelCount));

    public IPlatformAudioOutput? CreateDefault(PlatformAudioOutputOptions options)
    {
        if (OperatingSystem.IsWindows() && options.SampleRate == 48000 && options.ChannelCount == 2)
        {
            try { return new WindowsAudioOutput(options.BufferMilliseconds); }
            catch (InvalidOperationException) { /* Try the desktop fallback device. */ }
        }
        return OpenAlAudioOutput.TryCreate(out var output, options.SampleRate, options.ChannelCount,
            options.BufferMilliseconds) ? output : null;
    }
}
