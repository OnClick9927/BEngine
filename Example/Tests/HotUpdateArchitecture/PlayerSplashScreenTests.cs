using System.Diagnostics;
using BEngine.Player;

namespace BEngine.ExampleTests.HotUpdateArchitecture;

internal static class PlayerSplashScreenTests
{
    internal static void Run(string repositoryRoot)
    {
        if (!OperatingSystem.IsWindows()) return;
        var imagePath = Path.Combine(repositoryRoot,
            "src", "Core", "Editor", "Icons", "BEngine.png");
        var timer = Stopwatch.StartNew();
        using var splash = PlayerSplashScreen.Show(
            imagePath, "BEngine Splash Test", "#172126", TimeSpan.FromMilliseconds(120));
        Require(splash.IsVisible, "The Windows Player splash did not create a visible native window.");
        splash.CloseAfterMinimumDuration();
        Require(timer.Elapsed >= TimeSpan.FromMilliseconds(100),
            "The Player splash ignored its configured minimum duration.");
        Require(!splash.IsVisible, "The Windows Player splash remained visible after Close.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
