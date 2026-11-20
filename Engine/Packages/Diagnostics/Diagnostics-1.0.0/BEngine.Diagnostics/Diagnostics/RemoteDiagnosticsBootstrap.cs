using BEngine.Instrumentation;
using BEngine.Diagnostics;
using BEngine.Platform;

namespace BEngine.App.Diagnostics;

public static class RemoteDiagnosticsHost
{
    public static void StartFromEnvironment() => RemoteDiagnosticsBootstrap.Initialize();
    public static void Stop() => RemoteDiagnosticsBootstrap.Stop();
}

internal static class RemoteDiagnosticsBootstrap
{
    private static IRemoteDiagnosticsServer? _server;
    private static IDisposable? _unloading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    internal static void Initialize()
    {
        if (_server is not null) return;
        var platform = PlatformUtility.Current;
        var arguments = platform.GetCommandLineArgs().ToArray();
        var portText = Value(arguments, "--diagnostics-port") ??
                       platform.GetEnvironmentVariable("BENGINE_DIAGNOSTICS_PORT");
        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535) return;
        var host = Value(arguments, "--diagnostics-host") ??
                   platform.GetEnvironmentVariable("BENGINE_DIAGNOSTICS_HOST") ??
                   "127.0.0.1";
        var token = Value(arguments, "--diagnostics-token") ??
                    platform.GetEnvironmentVariable("BENGINE_DIAGNOSTICS_TOKEN") ?? string.Empty;
        _server = RemoteDiagnosticsServer.Start(host, port, token);
        Application.quitting += Stop;
        _unloading = DiagnosticsPlatform.ObserveAssemblyUnloading(typeof(RemoteDiagnosticsBootstrap).Assembly, Stop);
        Debug.Log($"REMOTE_DIAGNOSTICS_LISTENING|{_server.EndPoint.Address}:{_server.EndPoint.Port}");
    }

    private static string? Value(string[] arguments, string name)
    {
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (argument.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return argument[(name.Length + 1)..];
            if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase) &&
                index + 1 < arguments.Length)
                return arguments[index + 1];
        }
        return null;
    }

    internal static void Stop()
    {
        Application.quitting -= Stop;
        Interlocked.Exchange(ref _server, null)?.Dispose();
        Interlocked.Exchange(ref _unloading, null)?.Dispose();
    }
}
