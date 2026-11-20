using BEngine;
using BEngine.Audio;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;

public static class FactoryProbe
{
    public static string[] Run(string nativeRoot)
    {
        var details=new List<string>();
        void Check(bool value,string text){if(!value)throw new Exception(text);details.Add(text);}
        var factory=new TrackingFactory();
        using var services=new ServiceCollection().AddBEngineDotNetPlatform().AddSingleton<IPlatformNativeLibraryFactory>(factory).BuildServiceProvider();
        PlatformRuntime.Initialize(services);
        var path=Path.Combine(nativeRoot,"fixture.dll");
        var registration=AudioDspPlugins.RegisterNative("injected",path);
        Check(factory.Loads==1&&factory.Active==1,"Audio uses the explicitly supplied neutral module factory");
        Check(factory.Exports.Count==6&&factory.Exports[0]=="bengine_dsp_abi_version","The domain chooses all six ABI exports through the neutral lease");
        var instance=AudioDspPlugins.Create("injected",48000,2);
        registration.Dispose();Check(factory.Active==1,"An active instance pins its original platform lease");
        instance.Dispose();Check(factory.Active==0&&factory.Disposals==1,"Final instance disposal closes exactly the original lease");
        try{AudioDspPlugins.RegisterNative("wrong",Path.Combine(nativeRoot,"wrongabi.dll"));throw new Exception("Expected ABI failure");}catch(NotSupportedException){}
        Check(factory.Active==0&&factory.Disposals==2,"Constructor failure closes the injected platform lease");
        DotNetPlatformServices.Initialize();
        Check(RuntimeServices.GetRequiredService<IPlatformNativeLibraryFactory>() is SystemNativeLibraryFactory,"The normal host provides the system module capability");
        return details.ToArray();
    }
    private sealed class TrackingFactory:IPlatformNativeLibraryFactory
    {
        public int Loads,Active,Disposals;public readonly List<string> Exports=[];
        public IPlatformNativeLibrary Load(string path){var inner=SystemNativeLibraryFactory.Shared.Load(path);Loads++;Active++;return new Lease(this,inner);}
        private sealed class Lease(TrackingFactory owner,IPlatformNativeLibrary inner):IPlatformNativeLibrary
        {
            private bool _disposed;
            public nint GetExport(string name){owner.Exports.Add(name);return inner.GetExport(name);}
            public void Dispose(){if(_disposed)return;_disposed=true;inner.Dispose();owner.Active--;owner.Disposals++;}
        }
    }
}
