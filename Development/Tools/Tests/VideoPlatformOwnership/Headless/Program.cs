using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BEngine;
using BEngine.Hosting;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Video;

var checks=new List<string>();
void Check(bool condition,string name){if(!condition)throw new Exception(name);checks.Add(name);}
DotNetPlatformServices.Initialize();
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="BEngine.Platform.Desktop"),"Headless process does not load Desktop platform assembly");
Check(!Assembly.GetExecutingAssembly().GetReferencedAssemblies().Any(a=>a.Name=="BEngine.Platform.Desktop"),"Headless consumer has no Desktop reference");
Check(PlatformRuntime.GetService<IHostWindowFactory>()==null,"Headless host has no window factory");
Check(PlatformRuntime.GetRequiredService<IPlatformVideoDecoder>() is WindowsMediaDecoder,"DotNet-only host registers the Windows system decoder");
Check(VideoPlaybackCapabilities.Current.OriginalContainers,"Native video capabilities are available without a window");
Check(!FfmpegVideoFrameReader.IsAvailable,"Optional FFmpeg is absent; system decoder supplies the codec");
var clip=VideoClip.Load(args[1],false);var cursor=clip.OpenReader(VideoDecodeMode.Software);
var pixels=new byte[cursor.width*cursor.height*4];cursor.ReadFrame(0,pixels);
var firstHash=Convert.ToHexString(SHA256.HashData(pixels));
cursor.ReadFrame(Math.Min(1,cursor.duration),pixels);cursor.ReadFrame(0,pixels);
Check(Convert.ToHexString(SHA256.HashData(pixels))==firstHash,"Windowless software MP4 decode and reverse seek are deterministic");
Check(cursor.decodedFrames>0&&cursor.hardwareDecodedFrames==0,"Windowless software decoder produces actual counters");
BObject.DestroyImmediate(clip);
Check(VideoDecoder.activeNativeSessions==0&&VideoRuntimeSettings.activeDecoders==0&&VideoRuntimeSettings.estimatedMemoryBytes==0,"Windowless Clip retirement releases sessions and budget");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="BEngine.Platform.Desktop"),"Decode never loads a desktop window or rendering host");
File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=true,Count=checks.Count,Checks=checks,FirstFrameSHA256=firstHash},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("HEADLESS_MEDIA_OK|"+checks.Count);
