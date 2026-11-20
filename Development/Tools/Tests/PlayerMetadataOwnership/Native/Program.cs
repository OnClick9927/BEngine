using System.Text.Json;
using BEngine.Platform.DotNet;
DotNetPlatformServices.Initialize();
var checks=MetadataCoreProbe.Run(Path.GetFullPath(args[0]));
File.WriteAllText(args[1],JsonSerializer.Serialize(new{Passed=true,Count=checks.Count,Checks=checks}));
Console.WriteLine("CORE_METADATA_OK|"+checks.Count);
