using System.Diagnostics.CodeAnalysis;

namespace BEngine.Rendering.Rhi;

public sealed class GraphicsDeviceFactory
{
    private readonly Dictionary<GraphicsBackend, IGraphicsDeviceProvider> _providers = [];

    public IEnumerable<GraphicsBackend> RegisteredBackends => _providers.Keys;

    public void RegisterProvider(IGraphicsDeviceProvider provider, bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!replace && _providers.ContainsKey(provider.Backend))
            throw new InvalidOperationException($"A provider for {provider.Backend} is already registered.");
        _providers[provider.Backend] = provider;
    }

    public bool RemoveProvider(GraphicsBackend backend) => _providers.Remove(backend);

    public GraphicsBackendSupport QuerySupport(GraphicsBackend backend)
    {
        if (_providers.TryGetValue(backend, out var provider)) return provider.QuerySupport();
        var descriptor = GraphicsBackendCatalog.Get(backend);
        return descriptor.HasBuiltInProvider
            ? new GraphicsBackendSupport(backend, GraphicsBackendAvailability.Unavailable,
                $"{descriptor.DisplayName} requires a host-created provider with a current native graphics context.")
            : new GraphicsBackendSupport(backend, GraphicsBackendAvailability.NotImplemented,
                $"{descriptor.DisplayName} has an RHI contract but no provider is implemented in BEngine.Rendering.");
    }

    public IGraphicsDevice CreateDevice(GraphicsBackend backend)
    {
        if (!_providers.TryGetValue(backend, out var provider))
        {
            var support = QuerySupport(backend);
            throw new NotSupportedException(support.Reason);
        }

        var providerSupport = provider.QuerySupport();
        if (!providerSupport.CanCreateDevice) throw new NotSupportedException(providerSupport.Reason);
        var device = provider.CreateDevice() ??
                     throw new InvalidOperationException($"The {backend} provider returned no graphics device.");
        if (device.Backend != backend)
        {
            device.Dispose();
            throw new InvalidOperationException(
                $"The {backend} provider created a {device.Backend} device.");
        }
        return device;
    }

    public bool TryCreateDevice(
        GraphicsBackend backend,
        [NotNullWhen(true)] out IGraphicsDevice? device,
        out string? error)
    {
        try
        {
            device = CreateDevice(backend);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            device = null;
            error = exception.Message;
            return false;
        }
    }
}
