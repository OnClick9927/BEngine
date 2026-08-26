using System.Reflection;
using BEngine;
using BEngine.Editor;
using BEngine.Editor.Codex;

namespace BEngine.ExampleTests.CodexBuiltIn;

internal static class Program
{
    private static int Main()
    {
        try
        {
            var repository = FindRepositoryRoot();
            var hostAssembly = typeof(EditorWindow).Assembly;
            var codexAssembly = typeof(CodexProtocol).Assembly;
            Require(ReferenceEquals(hostAssembly, codexAssembly) &&
                    codexAssembly.GetName().Name == "BEngine.Editor",
                "Codex API is not owned by BEngine.Editor.");
            Require(hostAssembly.GetTypes().Any(type =>
                    type.Namespace?.StartsWith("BEngine.Editor.Codex", StringComparison.Ordinal) == true),
                "BEngine.Editor does not contain the Codex types.");

            var window = codexAssembly.GetType("BEngine.Editor.Codex.CodexEditorWindow", true)!;
            var menu = window.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .SelectMany(method => method.GetCustomAttributes<MenuItemAttribute>())
                .SingleOrDefault(attribute => attribute.itemName == "Window/Codex");
            Require(menu is not null, "The extension assembly did not register Window/Codex.");

            var coreRoot = Path.Combine(repository, "src", "Core");
            Resources.RegisterResourceRoot(coreRoot);
            var icon = EditorResource.FindPath("Icons/Windows/Codex.png");
            Require(icon is not null && Path.GetFullPath(icon).StartsWith(
                    Path.GetFullPath(coreRoot), StringComparison.OrdinalIgnoreCase),
                "The Codex icon is not loaded from Core/Editor.");
            var protocolEvent = CodexProtocol.ParseEvent(
                "{\"method\":\"turn/started\",\"params\":{\"turn\":{\"id\":\"test-turn\"}}}");
            Require(protocolEvent.Kind == CodexProtocolEventKind.TurnStarted,
                "Codex protocol behavior changed during package extraction.");

            var packagePaths = Directory.EnumerateFiles(
                Path.Combine(repository, "Output", "Packages"), "package.yaml", SearchOption.AllDirectories);
            Require(packagePaths.All(path => !File.ReadAllText(path).Contains(
                    "com.bengine.codex", StringComparison.OrdinalIgnoreCase)),
                "Codex is still exposed through Output/Packages.");

            Console.WriteLine("CODEX_CORE_OK|bengine-editor,menu,icon,protocol,not-a-package");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"CODEX_CORE_FAILED|{exception}");
            return 1;
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("BEngine repository root was not found.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
