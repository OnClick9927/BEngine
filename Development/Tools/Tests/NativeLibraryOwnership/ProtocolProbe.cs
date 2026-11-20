using System.Diagnostics;
using System.Runtime.InteropServices;
using BEngine.Audio;

public sealed record DspProbeResult(List<string> Details,long AllocatedBytes,double Milliseconds,float[] Samples);
public static class DspProtocolProbe
{
    [DllImport("kernel32",CharSet=CharSet.Unicode)] private static extern nint GetModuleHandle(string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Counter();
    public static DspProbeResult Run(string nativeRoot)
    {
        var checks=new List<string>();
        void Trace(string text){var output=Environment.GetEnvironmentVariable("BENGINE_SHADER_RESULT");if(!string.IsNullOrEmpty(output))File.AppendAllText(Path.Combine(output,"NativeLibraryProbeTrace.log"),text+Environment.NewLine);}
        void Check(bool value,string text){if(!value){Trace("FAILED: "+text);throw new Exception(text);}checks.Add(text);Trace(text);}
        void Throws<T>(Action action,string text)where T:Exception{try{action();}catch(T){Check(true,text);return;}throw new Exception(text);}
        var path=Path.GetFullPath(Path.Combine(nativeRoot,"fixture.dll"));
        Check(GetModuleHandle(path)==0,"Native fixture starts unloaded");
        Throws<InvalidOperationException>(()=>AudioDspPlugins.Create("ownership",48000,2),"Unknown plugin identifiers are rejected");
        var registration=AudioDspPlugins.RegisterNative("ownership",path);
        var module=GetModuleHandle(path);Check(module!=0,"Registering the real DSP opens a native module");
        Trace("Binding native counter delegates");
        var live=Marshal.GetDelegateForFunctionPointer<Counter>(NativeLibrary.GetExport(module,"fixture_live"));
        var destroyed=Marshal.GetDelegateForFunctionPointer<Counter>(NativeLibrary.GetExport(module,"fixture_destroyed"));
        var first=AudioDspPlugins.Create("ownership",48000,2);var second=AudioDspPlugins.Create("ownership",48000,1);
        Check(live()==2,"Each effect owns an independent native state");
        var samples=new float[]{1,2,3,4};first.Process(samples,new float[]{.5f});
        Check(samples.AsSpan().SequenceEqual(new float[]{.5f,1.125f,1.75f,2.375f}),"Gain and stateful processing preserve exact interleaved samples");
        var independent=new float[]{1,2};second.Process(independent,new float[]{3});
        Check(independent.AsSpan().SequenceEqual(new float[]{3,6.125f}),"A second instance has separate history and channel format");
        var continuation=new float[]{1,1};first.Process(continuation,new float[]{1});
        Check(continuation.AsSpan().SequenceEqual(new float[]{1.5f,1.625f}),"State survives successive native blocks");
        first.Process(continuation,ReadOnlySpan<float>.Empty);
        Check(continuation.AsSpan().SequenceEqual(new float[]{.75f,.875f}),"Shrinking the parameter list resets removed parameters");
        Check(first.HasTail&&second.HasTail,"Effect tails remain visible through the public interface");
        Throws<ArgumentException>(()=>first.Process(new float[3],new float[]{1}),"Partial interleaved frames are rejected");
        Throws<InvalidOperationException>(()=>AudioDspPlugins.Create("ownership",96000,2),"Native format rejection leaves no leaked state");
        Check(live()==2,"Failed creation preserves existing instances");
        Throws<InvalidOperationException>(()=>AudioDspPlugins.RegisterNative("ownership",path),"Duplicate registration rejects and closes its extra module lease");
        registration.Dispose();registration.Dispose();
        Throws<InvalidOperationException>(()=>AudioDspPlugins.Create("ownership",48000,2),"Registration disposal prevents new instances");
        Check(GetModuleHandle(path)!=0,"Unregistering retains the module for live instances");
        using(var replacement=AudioDspPlugins.RegisterNative("ownership",path))
        {
            registration.Dispose();using var third=AudioDspPlugins.Create("ownership",48000,1);
            Check(live()==3,"An old registration cannot remove a replacement registration");
        }
        Check(live()==2&&destroyed()==1,"Replacement instances and registration release their own leases");
        var parameters=new float[]{1};
        using var loopBudget=BEngine.LoopExecutionGuard.AllowIterations(5000);
        Trace("DSP warmup begin");
        for(var i=0;i<2000;i++)first.Process(samples,parameters);
        Trace("DSP warmup complete; measured loop begin");
        var before=GC.GetAllocatedBytesForCurrentThread();var timer=Stopwatch.GetTimestamp();
        for(var i=0;i<5000;i++){samples.AsSpan().Fill(.25f);first.Process(samples,parameters);}
        var milliseconds=Stopwatch.GetElapsedTime(timer).TotalMilliseconds;
        var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Trace("DSP measured loop complete: "+allocated+" B");
        Check(allocated==0,"Warmed DSP processing performs no managed allocation");
        first.Dispose();first.Dispose();
        Check(live()==1&&destroyed()==2,"Repeated disposal destroys a native instance once");
        Throws<ObjectDisposedException>(()=>first.Process(samples,parameters),"Disposed DSP instances reject processing");
        Check(!first.HasTail,"Disposed instances have no tail");
        second.Process(independent,parameters);Check(GetModuleHandle(path)!=0,"The last instance still processes after registration retirement");
        second.Dispose();Check(GetModuleHandle(path)==0,"The module unloads only after the last registration and instance release");
        var wrong=Path.GetFullPath(Path.Combine(nativeRoot,"wrongabi.dll"));
        Throws<NotSupportedException>(()=>AudioDspPlugins.RegisterNative("wrong",wrong),"Unsupported ABI versions are rejected");
        Check(GetModuleHandle(wrong)==0,"ABI rejection releases its opened module");
        var missing=Path.GetFullPath(Path.Combine(nativeRoot,"missing.dll"));
        Throws<EntryPointNotFoundException>(()=>AudioDspPlugins.RegisterNative("missing",missing),"Missing required exports are rejected");
        Check(GetModuleHandle(missing)==0,"Partial export binding releases its opened module");
        Throws<ArgumentException>(()=>AudioDspPlugins.RegisterNative(" ",path),"Invalid registration identifiers remain rejected");
        Check(GetModuleHandle(path)==0,"A rejected registration leaves no loaded module");
        return new(checks,allocated,milliseconds,samples);
    }
}
