using BEngine.Player;
using BEngine.Player.Hosts.Desktop;
using BEngine.Platform;
using System;

if (OperatingSystem.IsWindows()) WindowsPlatform.Initialize();
else if (OperatingSystem.IsMacOS()) macOSPlatform.Initialize();
else LinuxPlatform.Initialize();

return BEnginePlayer.Run(new DesktopPlayerSplashHost(), BEnginePlayer.FilterHostArguments(args));
