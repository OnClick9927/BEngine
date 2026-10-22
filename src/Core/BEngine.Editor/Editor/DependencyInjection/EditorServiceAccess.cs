using Microsoft.Extensions.DependencyInjection;
using BEngine.DependencyInjection;

namespace BEngine.Editor;

internal static class EditorServiceAccess
{
    private static ServiceProvider? _fallback;
    private static int _fallbackVersion = -1;
    private static ServiceProvider CreateFallback() =>
        new ServiceCollection()
            .AddBEnginePlatformServices()
            .AddBEngineEditorCompilation()
            .AddBEngineEditorPlatformServices()
            .BuildServiceProvider();
    private static Attachment? _lastAttachment;

    internal static T GetRequiredService<T>() where T : notnull
    {
        if (_lastAttachment is { } attached) return attached.Services.GetRequiredService<T>();
        if (_fallback is null || _fallbackVersion != RuntimeServiceContributions.Version)
        {
            var replacement = CreateFallback();
            var previous = _fallback;
            _fallback = replacement;
            _fallbackVersion = RuntimeServiceContributions.Version;
            previous?.Dispose();
        }
        return _fallback.GetRequiredService<T>();
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
            _runtimeAttachment.Dispose();
        }
    }
}
