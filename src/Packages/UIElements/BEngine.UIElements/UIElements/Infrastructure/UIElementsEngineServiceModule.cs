using BEngine.DependencyInjection;
using BEngine.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.UIElements;

public sealed class UIElementsEngineServiceModule : IEngineServiceModule
{
    public void ConfigureServices(IServiceCollection services, EngineServiceContext context)
    {
        services.TryAddSingleton<IUIDocumentSourceLoader, FileUIDocumentSourceLoader>();
        services.TryAddSingleton<IUIRenderResourceResolver, FileUIRenderResourceResolver>();
    }
}
