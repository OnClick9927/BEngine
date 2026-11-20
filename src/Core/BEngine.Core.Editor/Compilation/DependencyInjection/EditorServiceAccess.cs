using Microsoft.Extensions.DependencyInjection;
using BEngine.DependencyInjection;

namespace BEngine.Editor;

internal static class EditorServiceAccess
{
    private static EditorContributionServices? _fallback;
    private static int _fallbackVersion = -1;
    private static IServiceProvider? _fallbackHost;
    private static EditorContributionServices CreateFallback(IServiceProvider? host)
    {
        var inherited = host?.GetService<EditorServiceRegistrationCatalog>();
        var services = new ServiceCollection();
        if (inherited is not null)
            foreach (var registration in inherited.Registrations) ((IServiceCollection)services).Add(registration);
        // This entry also registers platform contributions. Invoke it once so
        // additive registrations are not duplicated in IEnumerable<T>.
        services.AddBEngineEditorPlatformServices().AddBEngineEditorCompilation();
        return new EditorContributionServices(host, services.Where(registration =>
            inherited?.Contains(registration) != true).ToArray());
    }
    private static Attachment? _lastAttachment;

    internal static T GetRequiredService<T>() where T : notnull =>
        (T)(Resolve(typeof(T)) ?? throw new InvalidOperationException($"No service for type '{typeof(T)}' has been registered."));

    internal static T? GetService<T>() where T : class => Resolve(typeof(T)) as T;

    private static object? Resolve(Type serviceType)
    {
        var enumerable = serviceType.IsConstructedGenericType &&
            serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>);
        if (!enumerable && _lastAttachment?.Services.GetService(serviceType) is { } service) return service;
        // The contribution scope also forwards constructor/factory dependencies
        // and combines enumerations without taking ownership of host instances.
        return Fallback.GetService(serviceType);
    }

    private static IServiceProvider Fallback
    {
        get
        {
            var host = _lastAttachment?.Services;
            if (_fallback is null || _fallbackVersion != RuntimeServiceContributions.Version ||
                !ReferenceEquals(_fallbackHost, host))
            {
                var replacement = CreateFallback(host);
                var previous = _fallback;
                _fallback = replacement;
                _fallbackVersion = RuntimeServiceContributions.Version;
                _fallbackHost = host;
                previous?.Dispose();
            }
            return _fallback;
        }
    }

    internal static IDisposable Attach(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var runtimeAttachment = services.AttachBEngineRuntimeServices();
        try
        {
            var attachment = new Attachment(services, runtimeAttachment, _lastAttachment);
            if (_lastAttachment is not null) _lastAttachment.Next = attachment;
            _lastAttachment = attachment;
            return attachment;
        }
        catch
        {
            runtimeAttachment.Dispose();
            throw;
        }
    }

    private sealed class Attachment : IDisposable
    {
        private readonly IDisposable _runtimeAttachment;
        private bool _disposed;

        internal Attachment(
            IServiceProvider services,
            IDisposable runtimeAttachment,
            Attachment? previous)
        {
            Services = services;
            _runtimeAttachment = runtimeAttachment;
            Previous = previous;
        }

        internal IServiceProvider Services { get; }
        internal Attachment? Previous { get; set; }
        internal Attachment? Next { get; set; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (Previous is not null) Previous.Next = Next;
            if (Next is not null) Next.Previous = Previous;
            if (ReferenceEquals(_lastAttachment, this)) _lastAttachment = Previous;

            Previous = null;
            Next = null;
            try
            {
                if (ReferenceEquals(_fallbackHost, Services) && !ReferenceEquals(_lastAttachment?.Services, Services))
                {
                    var previousFallback = _fallback;
                    _fallback = null;
                    _fallbackHost = null;
                    _fallbackVersion = -1;
                    previousFallback?.Dispose();
                }
            }
            finally { _runtimeAttachment.Dispose(); }
        }
    }
}
