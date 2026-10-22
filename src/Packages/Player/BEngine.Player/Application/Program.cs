namespace BEngine.Player;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (System.OperatingSystem.IsWindows()) BEngine.Platform.WindowsPlatform.Initialize();
        else if (System.OperatingSystem.IsMacOS()) BEngine.Platform.macOSPlatform.Initialize();
        else BEngine.Platform.LinuxPlatform.Initialize();
        return BEnginePlayer.Run(BEnginePlayer.FilterHostArguments(args));
    }
}
