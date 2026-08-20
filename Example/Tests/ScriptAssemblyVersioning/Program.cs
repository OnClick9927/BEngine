using BEngine.ProjectSystem;
using BEngine.Serialization;
using System.Runtime.Loader;

namespace BEngine.ExampleTests.ScriptAssemblyVersioning;

internal static class Program
{
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineScriptVersioning_{Guid.NewGuid():N}");
        try
        {
            var example = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "../../../../../..", "Example"));
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "Packages"));
            File.Copy(Path.Combine(example, "Project.yaml"), Path.Combine(root, "Project.yaml"));
            File.Copy(Path.Combine(example, "Packages", "manifest.yaml"),
                Path.Combine(root, "Packages", "manifest.yaml"));

            var workspace = ProjectWorkspace.Open(root);
            using var packageManager = new BPackageManager(workspace);
            File.WriteAllText(Path.Combine(workspace.ScriptsPath, "LockedOutputTest.cs"), """
                using BEngine;
                namespace Game;
                public sealed class LockedOutputTest : MonoBehaviour { }
                """);

            var legacyPath = Path.Combine(workspace.ScriptAssembliesPath, "GameScripts.dll");
            File.WriteAllBytes(legacyPath, [0x42]);
            using var legacyLock = new FileStream(
                legacyPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            var assembly = ProjectScriptCompiler.CompileAndLoad(workspace) ??
                           throw new InvalidOperationException("GameScripts was not compiled.");
            var currentPath = ScriptAssemblyStore.ResolveCurrentPath(workspace, "GameScripts") ??
                              throw new InvalidOperationException("Current script assembly was not published.");
            Assert(File.Exists(ScriptAssemblyStore.GetReferencePath(workspace, "GameScripts")),
                "Current YAML reference was not written.");
            Assert(!string.Equals(currentPath, legacyPath, StringComparison.OrdinalIgnoreCase),
                "Compiler still overwrote the legacy stable DLL.");
            Assert(string.Equals(Path.GetFullPath(assembly.Location), currentPath,
                    StringComparison.OrdinalIgnoreCase),
                "Loaded assembly does not match the published version.");
            Assert(AssemblyLoadContext.GetLoadContext(assembly)?.IsCollectible == true,
                "Project scripts must use a collectible load context so package dependencies can unload.");
            Assert(File.ReadAllBytes(legacyPath).SequenceEqual(new byte[] { 0x42 }),
                "Locked legacy output was modified.");

            Console.WriteLine(
                "SCRIPT_ASSEMBLY_VERSIONING_OK|locked-output,version-pointer,player-resolution,collectible-context");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SCRIPT_ASSEMBLY_VERSIONING_FAILED|{exception}");
            return 1;
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
