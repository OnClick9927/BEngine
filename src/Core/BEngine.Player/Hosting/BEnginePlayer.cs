using Microsoft.Extensions.DependencyInjection;
namespace BEngine.Player;
/// <summary>Engine entry point. The selected content workflow is contributed by a runtime module.</summary>
public static class BEnginePlayer
{
    public const string PersistentDataPathEnvironmentVariable = "BENGINE_PLAYER_PERSISTENT_DATA_PATH";
    public const string NativeDiagnosticsEnvironmentVariable = "BENGINE_PLAYER_NATIVE_DIAGNOSTICS";
    public static string[] FilterHostArguments(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string[] names = ["--diagnostics-host", "--diagnostics-port", "--diagnostics-token"];
        var result = new List<string>(arguments.Count);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            bool __queryResult1451;
            {
                var __querySource1451 = names;
                ArgumentNullException.ThrowIfNull(__querySource1451, "source");
                __queryResult1451 = false;
                foreach (var __queryItem1451 in __querySource1451)
                {
                    if ((string.Equals(argument, __queryItem1451, StringComparison.OrdinalIgnoreCase)))
                    {
                        __queryResult1451 = true;
                        break;
                    }
                }
            }
            
            var exact = __queryResult1451;
            if (exact)
            {
                if (index + 1 >= arguments.Count)
                    throw new ArgumentException($"A value is required after '{argument}'.");
                index++;
                continue;
            }
            bool __queryResult1823;
            {
                var __querySource1823 = names;
                ArgumentNullException.ThrowIfNull(__querySource1823, "source");
                __queryResult1823 = false;
                foreach (var __queryItem1823 in __querySource1823)
                {
                    if ((argument.StartsWith(__queryItem1823 + "=", StringComparison.OrdinalIgnoreCase)))
                    {
                        __queryResult1823 = true;
                        break;
                    }
                }
            }
            
            if (__queryResult1823)
                continue;
            result.Add(argument);
        }
        return result.ToArray();
    }


    public static int Run(params string[] args) => Run(new ServiceCollection(), args);
    public static int Run(IServiceCollection services, params string[] args) => PlayerHostRegistry.Current.Run(services, args);
    public static string ResolveDefaultProjectPath(string? playerDirectory = null) => ResolveDefaultProjectPath(new ServiceCollection(), playerDirectory);
    public static string ResolveDefaultProjectPath(IServiceCollection services, string? playerDirectory = null) => PlayerHostRegistry.Current.ResolveDefaultProjectPath(services, playerDirectory);
    public static IPlayerRuntimeSession CreateRuntimeSession(string projectPath, bool declareAotInterpreter = false, string? persistentDataPath = null) =>
        CreateRuntimeSession(new ServiceCollection(), projectPath, declareAotInterpreter, persistentDataPath);
    public static IPlayerRuntimeSession CreateRuntimeSession(IServiceCollection services, string projectPath, bool declareAotInterpreter = false, string? persistentDataPath = null) =>
        PlayerHostRegistry.Current.CreateRuntimeSession(services, projectPath, declareAotInterpreter, persistentDataPath);
    public static void Validate(string projectPath, bool declareAotInterpreter = false, string? persistentDataPath = null) => Validate(new ServiceCollection(), projectPath, declareAotInterpreter, persistentDataPath);
    public static void Validate(IServiceCollection services, string projectPath, bool declareAotInterpreter = false, string? persistentDataPath = null) => PlayerHostRegistry.Current.Validate(services, projectPath, declareAotInterpreter, persistentDataPath);
}
public static class PlayerHostRegistry
{
    private static IPlayerHost? _current;
    private static Type? _registrationType;
    public static IPlayerHost Current
    {
        get
        {
            PlayerRuntimeModules.Initialize();
            return _current ?? throw new InvalidOperationException("No Player content workflow is registered. Include a runtime host module in the build.");
        }
    }
    public static void Unregister(System.Reflection.Assembly owner)
    {
        if (_registrationType?.Assembly == owner)
        {
            _current = null;
            _registrationType = null;
        }
    }
    public static void Register(IPlayerHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Register(host, host.GetType());
    }
    /// <summary>Uses the Core-owned host with content contributed by the registering module.</summary>
    public static void RegisterContentWorkflow(IPlayerContentWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        Register(new PlayerHost(workflow), workflow.GetType());
    }
    private static void Register(IPlayerHost host, Type registrationType)
    {
        if (_current is not null && _registrationType != registrationType)
            throw new InvalidOperationException("A different Player host is already registered.");
        _current = host;
        _registrationType = registrationType;
    }
}
