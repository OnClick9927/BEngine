using BEngine;
using BEngine.Player.HotUpdate;
namespace SessionFixture;
public sealed class Module:IHotUpdateModule
{
    public static int Configured,Started,Stopped;
    public HotUpdateModuleDescriptor Descriptor {get;}=new(){ModuleId="fixture"};
    public BValueTask ConfigureAsync(IHotUpdateModuleContext context,BCancellationToken token=default)
    {Configured++;context.Services.Export<IServiceProvider>(context.HostServices);return BValueTask.CompletedTask;}
    public BValueTask StartAsync(BCancellationToken token=default){Started++;return BValueTask.CompletedTask;}
    public BValueTask StopAsync(BCancellationToken token=default){Stopped++;return BValueTask.CompletedTask;}
}
