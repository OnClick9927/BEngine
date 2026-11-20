using System.Text.Json;
using BEngine;
using BEngine.Platform;
using BEngine.Platform.DotNet;

DotNetPlatformServices.Initialize();
var root = Path.GetFullPath(args[0]);
var checks = LoggingProbe.Run(root, RuntimeServices.GetRequiredService<FileUtility>(),
    RuntimeServices.GetRequiredService<PathUtility>(), RuntimeServices.GetRequiredService<PlatformUtility>(),
    RuntimeServices.GetRequiredService<IPlatformService>());
checks.AddRange(ModuleManifestProbe.Run(Path.Combine(root, "module-manifests")));
File.WriteAllText(args[1], JsonSerializer.Serialize(new { Passed = true, Count = checks.Count, Checks = checks }));
Console.WriteLine($"CORE_PLAYER_LOG_OK|{checks.Count}");
