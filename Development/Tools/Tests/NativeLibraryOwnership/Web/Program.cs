using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using BEngine;
using BEngine.Audio;
using BEngine.Platform;
using BEngine.Platform.DotNet;

if(OperatingSystem.IsBrowser())Probe.Run();
[SupportedOSPlatform("browser")]
internal static partial class Probe
{
    [JSImport("globalThis.nativeModuleProbeResult")] private static partial void Report(string json);
    internal static void Run()
    {
        var checks=new List<string>();
        void Check(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
        try
        {
            DotNetPlatformServices.Initialize();
            var factory=RuntimeServices.GetRequiredService<IPlatformNativeLibraryFactory>();
            Check(factory.GetType().Assembly.GetName().Name=="BEngine.Platform.DotNet","Browser default module capability belongs to its platform host");
            try{factory.Load("nonexistent.dll");throw new Exception("Expected unsupported target");}catch(PlatformNotSupportedException){Check(true,"Browser rejects dynamic loading before attempting file IO");}
            try{AudioDspPlugins.RegisterNative("native","nonexistent.dll");throw new Exception("Expected unsupported DSP");}catch(PlatformNotSupportedException){Check(true,"Audio receives the platform capability failure without a platform branch");}
            var effect=new ManagedEffect();
            using var registration=AudioDspPlugins.Register("managed",(_,_)=>effect);
            using var instance=AudioDspPlugins.Create("managed",48000,1);
            var samples=new float[]{1,2};instance.Process(samples,new float[]{.5f});
            Check(samples[0]==.5f&&samples[1]==1&&instance.HasTail,"Managed DSP remains usable on browser targets");
            instance.Dispose();Check(effect.Disposed,"Managed DSP disposal preserves its public lifecycle");
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.Graphics" or "BEngine.Core.Editor"),"Module capability requires no rendering or Editor package");
            Report(JsonSerializer.Serialize(new ProbeResult(true,checks.Count,checks,null),ProbeJson.Default.ProbeResult));
        }
        catch(Exception error){Report(JsonSerializer.Serialize(new ProbeResult(false,checks.Count,checks,error.ToString()),ProbeJson.Default.ProbeResult));}
    }
    private sealed class ManagedEffect:IAudioDspPlugin
    {
        public bool Disposed;public bool HasTail=>!Disposed;
        public void Process(Span<float> samples,ReadOnlySpan<float> parameters){foreach(ref var sample in samples)sample*=parameters[0];}
        public void Dispose()=>Disposed=true;
    }
}
internal sealed record ProbeResult(bool Passed,int Checks,List<string> Details,string? Error);
[JsonSerializable(typeof(ProbeResult))] internal partial class ProbeJson:JsonSerializerContext;
