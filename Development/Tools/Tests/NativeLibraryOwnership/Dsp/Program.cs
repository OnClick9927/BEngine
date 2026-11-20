using System.Text.Json;
using BEngine.Platform.DotNet;
var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
DotNetPlatformServices.Initialize();
var result=DspProtocolProbe.Run(args[1]);
#if !LEGACY
var factory=FactoryProbe.Run(args[1]);
#else
var factory=Array.Empty<string>();
#endif
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=result.Details.Count+factory.Length,Protocol=result,Factory=factory},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("NATIVE_DSP_OK "+(result.Details.Count+factory.Length));
