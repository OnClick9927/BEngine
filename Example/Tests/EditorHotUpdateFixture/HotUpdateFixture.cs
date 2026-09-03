using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.ExampleTests.EditorHotUpdateFixture;

public sealed class HotDomainProbePayload
{
    public string Label = string.Empty;
    public int Value;
}

public sealed class HotDomainProbeComponent : MonoBehaviour
{
    public int Counter;
    public HotDomainProbePayload Payload = new();
}

public sealed class HotDomainProbeService
{
    public string Token => "hot-domain-service";
}

public sealed class HotDomainProbeServiceModule : IEngineServiceModule
{
    public static bool Configured { get; private set; }

    public void ConfigureServices(IServiceCollection services, EngineServiceContext context)
    {
        Configured = true;
        services.AddSingleton<HotDomainProbeService>();
    }
}

public sealed class HotDomainProbeRuntimeSystem(HotDomainProbeService service) : ISceneRuntimeSystem
{
    public static bool Started { get; private set; }

    public void Start(Scene scene) => Started = service.Token == "hot-domain-service";

    public void Stop(Scene scene) => Started = false;
}
