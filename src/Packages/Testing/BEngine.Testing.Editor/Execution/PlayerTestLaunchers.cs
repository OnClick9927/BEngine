using BEngine.Build;

namespace BEngine.Testing.Editor;

public sealed record PlayerTestLaunchRequest(PlayerBuildResult Build, string ContentUrl, string ResultPath,
    bool CollectCoverage, IReadOnlyCollection<string>? TestIds)
{
    public string AndroidApplicationIdentifier { get; init; } = "com.bengine.game";
    /// <summary>The owning project. Browser profiles and other test intermediates belong to its Temp directory.</summary>
    public string ProjectPath { get; init; } = string.Empty;
}

/// <summary>Device/browser runners can install, launch and return results without changing the test pipeline.</summary>
public static class PlayerTestLaunchers
{
    private static readonly Dictionary<string, Func<PlayerTestLaunchRequest, CancellationToken, Task<TestRunResult>>> Launchers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();
    public static void Register(string targetId, Func<PlayerTestLaunchRequest, CancellationToken, Task<TestRunResult>> launcher)
    { ArgumentException.ThrowIfNullOrWhiteSpace(targetId); ArgumentNullException.ThrowIfNull(launcher); lock (Gate) { if (!Launchers.TryAdd(targetId, launcher)) throw new InvalidOperationException($"Test launcher '{targetId}' already exists."); } }
    public static bool Unregister(string targetId) { lock (Gate) return Launchers.Remove(targetId); }
    internal static bool TryGet(string targetId, out Func<PlayerTestLaunchRequest, CancellationToken, Task<TestRunResult>> launcher)
    {
        lock (Gate) if (Launchers.TryGetValue(targetId, out launcher!)) return true;
        if (targetId.StartsWith("android", StringComparison.OrdinalIgnoreCase)) { launcher = AndroidPlayerTestLauncher.Run; return true; }
        if (targetId.StartsWith("web", StringComparison.OrdinalIgnoreCase)) { launcher = WebPlayerTestLauncher.Run; return true; }
        launcher = null!; return false;
    }
}
