using BEngine.Player;
using BEngine.Hosting;
using BEngine.Platform;
using System;

namespace BEngine.Player.Hosts.Desktop;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (OperatingSystem.IsWindows()) WindowsPlatform.Initialize();
        else if (OperatingSystem.IsMacOS()) macOSPlatform.Initialize();
        else LinuxPlatform.Initialize();

        var arguments = BEnginePlayer.FilterHostArguments(args);
        using var startup = arguments.Length == 0 || arguments[0] != "--validate"
            ? HostStartupSession.Begin() : null;
        return BEnginePlayer.Run(arguments);
    }
}
