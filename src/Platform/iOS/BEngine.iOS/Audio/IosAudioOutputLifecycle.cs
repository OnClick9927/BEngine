namespace BEngine.Platform;

internal static class IosAudioOutputLifecycle
{
    private static readonly object Gate = new();
    private static readonly HashSet<IosAudioPlatformOutput> Outputs = [];

    internal static void Register(IosAudioPlatformOutput output)
    {
        lock (Gate) Outputs.Add(output);
    }

    internal static void Unregister(IosAudioPlatformOutput output)
    {
        lock (Gate) Outputs.Remove(output);
    }

    internal static void Suspend()
    {
        lock (Gate)
            foreach (var output in Outputs) output.SuspendForInterruption();
    }

    internal static void Resume()
    {
        lock (Gate)
            foreach (var output in Outputs) output.ResumeAfterInterruption();
    }
}
