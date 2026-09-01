using System.Runtime.InteropServices;

namespace BEngine.ProjectSystem;

internal static class BEngineCompilationSymbols
{
    internal const string DebugConfiguration = "Debug";
    internal const string ReleaseConfiguration = "Release";

    internal static IReadOnlyList<string> Create(bool editor, string configuration)
    {
        var symbols = new HashSet<string>(StringComparer.Ordinal)
        {
            "BENGINE",
            "BENGINE_1_0",
            "BENGINE_1_0_OR_NEWER"
        };
        if (editor) symbols.Add("BENGINE_EDITOR");

        AddPlatformSymbols(symbols, ResolvePlatform(editor));
        AddArchitectureSymbols(symbols, RuntimeInformation.ProcessArchitecture);
        AddConfigurationSymbols(symbols, configuration);
        return symbols.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray();
    }

    internal static RuntimePlatform ResolvePlatform(bool editor)
    {
        if (OperatingSystem.IsWindows())
            return editor ? RuntimePlatform.WindowsEditor : RuntimePlatform.WindowsPlayer;
        if (OperatingSystem.IsMacOS())
            return editor ? RuntimePlatform.OSXEditor : RuntimePlatform.OSXPlayer;
        return editor ? RuntimePlatform.LinuxEditor : RuntimePlatform.LinuxPlayer;
    }

    private static void AddPlatformSymbols(ISet<string> symbols, RuntimePlatform platform)
    {
        var platformName = platform.ToString();
        var family = platformName
            .Replace("Editor", string.Empty, StringComparison.Ordinal)
            .Replace("Player", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        symbols.Add($"BENGINE_{family}");
    }

    private static void AddArchitectureSymbols(ISet<string> symbols, Architecture architecture)
    {
        symbols.Add($"BENGINE_{architecture.ToString().ToUpperInvariant()}");
    }

    private static void AddConfigurationSymbols(ISet<string> symbols, string configuration)
    {
        if (configuration.Equals(DebugConfiguration, StringComparison.OrdinalIgnoreCase))
        {
            symbols.Add("DEBUG");
            return;
        }
        if (configuration.Equals(ReleaseConfiguration, StringComparison.OrdinalIgnoreCase))
        {
            symbols.Add("RELEASE");
            return;
        }
        throw new ArgumentOutOfRangeException(nameof(configuration), configuration,
            "Only Debug and Release script configurations are supported.");
    }
}
