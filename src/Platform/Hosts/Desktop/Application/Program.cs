using BEngine.Player;
using BEngine.Player.Hosts.Desktop;
using BEngine.Platform;
using System;

if (OperatingSystem.IsWindows()) WindowsPlatform.Initialize();
else if (OperatingSystem.IsMacOS()) macOSPlatform.Initialize();
else LinuxPlatform.Initialize();

var arguments = BEnginePlayer.FilterHostArguments(args);
var host = new DesktopPlayerSplashHost();
using var earlySplash = arguments.Length == 0 || arguments[0] != "--validate"
    ? ShowEarlySplash() : null;
return BEnginePlayer.Run(host, arguments);

PlayerSplashScreen? ShowEarlySplash()
{
    if (PlayerStartupEnvironment.LogoPng.Length == 0) return null;
    PlayerStartupEnvironment.Begin();
    return PlayerSplashScreen.Show(PlayerStartupEnvironment.LogoPng, "BEngine",
        PlayerStartupEnvironment.BackgroundColor, TimeSpan.FromSeconds(.5), host);
}
