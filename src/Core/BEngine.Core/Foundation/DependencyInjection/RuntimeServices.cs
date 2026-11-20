using BEngine.Platform;

namespace BEngine;

/// <summary>Resolves borrowed services from the current scene, attached runtime scope, or platform host.</summary>
/// <remarks>Use on the engine thread. The service owner controls lifetime; callers must not dispose resolved services.</remarks>
public static class RuntimeServices
{
    private static Attachment? _lastAttachment;

    internal static FileUtility FileSystem =>
        GetRequiredService<FileUtility>();

    internal static PathUtility PathService =>
        GetRequiredService<PathUtility>();

    internal static PlatformUtility PlatformService =>
        GetRequiredService<PlatformUtility>();

    internal static ApplicationPathUtility ApplicationPaths =>
        GetRequiredService<ApplicationPathUtility>();

    internal static IDisposable Attach(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var attachment = new Attachment(services, _lastAttachment);
        if (_lastAttachment is not null) _lastAttachment.Next = attachment;
        _lastAttachment = attachment;
        return attachment;
    }

    /// <summary>Returns a service from the active scene, latest attached scope, or platform fallback, in that order.</summary>
    public static T? GetService<T>() where T : class
    {
        // A synchronous scene switch can unwind through a callback from the old scene.
        // Its runtime context may still be current after its owned service scope ended.
        if (SceneRuntime.currentScene is { isCreated: true } scene &&
            scene.Services.GetService(typeof(T)) is T sceneService)
            return sceneService;
        return _lastAttachment?.Services.GetService(typeof(T)) as T ?? PlatformRuntime.GetService<T>();
    }

    /// <summary>Resolves a service or throws when no active scope or platform registration provides it.</summary>
    public static T GetRequiredService<T>() where T : class =>
        GetService<T>() ?? throw new PlatformNotSupportedException(
            $"The platform host has not registered {typeof(T).Name}.");

    internal static T Resolve<T>(T fallback) where T : class =>
        GetService<T>() ?? fallback;

    private sealed class Attachment : IDisposable
    {
        internal Attachment(IServiceProvider services, Attachment? previous)
        {
            Services = services;
            Previous = previous;
        }

        internal IServiceProvider Services { get; }
        internal Attachment? Previous { get; set; }
        internal Attachment? Next { get; set; }

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (Previous is not null) Previous.Next = Next;
            if (Next is not null) Next.Previous = Previous;
            if (ReferenceEquals(_lastAttachment, this)) _lastAttachment = Previous;

            Previous = null;
            Next = null;
        }
    }
}
