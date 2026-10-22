using System.Diagnostics.CodeAnalysis;

namespace BEngine.Launcher;

// A UI timer consumes only the latest progress instead of posting every file's
// start/end notification to the Windows message queue during parallel downloads.
internal sealed class LauncherUpdateProgressBuffer : IProgress<LauncherUpdateProgress>
{
    private LauncherUpdateProgress? _latest;

    public void Report(LauncherUpdateProgress value) => Interlocked.Exchange(ref _latest, value);

    public bool TryTake([NotNullWhen(true)] out LauncherUpdateProgress? progress)
    {
        progress = Interlocked.Exchange(ref _latest, null);
        return progress is not null;
    }

    public void Clear() => Interlocked.Exchange(ref _latest, null);
}
