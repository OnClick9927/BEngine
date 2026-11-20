using Microsoft.Extensions.DependencyInjection;
namespace BEngine.Player;
/// <summary>Compatibility entry points selecting the AssetBundle content workflow.</summary>
public static class AssetBundlePlayerHost
{
    public const string PersistentDataPathEnvironmentVariable = BEnginePlayer.PersistentDataPathEnvironmentVariable;
    public const string NativeDiagnosticsEnvironmentVariable = BEnginePlayer.NativeDiagnosticsEnvironmentVariable;
    public static string[] FilterHostArguments(IReadOnlyList<string> arguments) => BEnginePlayer.FilterHostArguments(arguments);
    public static int Run(params string[] args) => AssetBundlePlayerWorkflow.Host.Run(args);
    public static int Run(IServiceCollection services, params string[] args) => AssetBundlePlayerWorkflow.Host.Run(services, args);
    public static string ResolveDefaultProjectPath(string? playerDirectory = null) => AssetBundlePlayerWorkflow.Host.ResolveDefaultProjectPath(playerDirectory);
    public static string ResolveDefaultProjectPath(IServiceCollection services, string? playerDirectory = null) => AssetBundlePlayerWorkflow.Host.ResolveDefaultProjectPath(services, playerDirectory);
    public static PlayerRuntimeSession CreateRuntimeSession(string projectPath, bool declareAotInterpreter = false, string? persistentDataPath = null) => AssetBundlePlayerWorkflow.Host.CreateRuntimeSession(projectPath, declareAotInterpreter, persistentDataPath);
    public static PlayerRuntimeSession CreateRuntimeSession(IServiceCollection services, string projectPath, bool declareAotInterpreter = false, string? persistentDataPath = null) => AssetBundlePlayerWorkflow.Host.CreateRuntimeSession(services, projectPath, declareAotInterpreter, persistentDataPath);
    public static void Validate(string projectPath, bool declareAotInterpreter = false, string? persistentDataPath = null) => AssetBundlePlayerWorkflow.Host.Validate(projectPath, declareAotInterpreter, persistentDataPath);
    public static void Validate(IServiceCollection services, string projectPath, bool declareAotInterpreter = false, string? persistentDataPath = null) => AssetBundlePlayerWorkflow.Host.Validate(services, projectPath, declareAotInterpreter, persistentDataPath);
}
