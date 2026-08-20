
namespace BEngine.Editor;

internal static class EditorBridge
{
    private static IEditorHost? _host;

    internal static IEditorHost? Host => Volatile.Read(ref _host);

    internal static void Attach(IEditorHost host) => Volatile.Write(ref _host, host);

    internal static void Detach(IEditorHost host)
    {
        Interlocked.CompareExchange(ref _host, null, host);
    }
}
