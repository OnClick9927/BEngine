using BEngine.Editor;
using BEngine.ProjectSystem;

namespace BEngine.ExampleTests.MultiInstanceProject;

internal static class Program
{
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineMultiInstance_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            new ProjectData { Name = "MultiInstance" }.Save(
                Path.Combine(root, ProjectWorkspace.ProjectFileName));
            var workspace = ProjectWorkspace.Open(root);
            using var first = EditorInstanceContext.Create(workspace.RootPath, makeCurrent: false);
            using var second = EditorInstanceContext.Create(workspace.RootPath, makeCurrent: false);

            Require(first.instanceId != second.instanceId, "Instance IDs collided.");
            Require(!PathsEqual(first.dataPath, second.dataPath), "Editor data paths are shared.");
            Require(!PathsEqual(first.logsPath, second.logsPath), "Editor log paths are shared.");
            Require(!PathsEqual(first.tempPath, second.tempPath), "Temporary build paths are shared.");
            Require(!PathsEqual(first.scriptAssembliesPath, second.scriptAssembliesPath),
                "Loaded script assembly paths are shared.");
            Require(!PathsEqual(first.packageCachePath, second.packageCachePath),
                "Package shadow-copy paths are shared.");
            Require(first.cachePath.StartsWith(workspace.LibraryPath, StringComparison.OrdinalIgnoreCase) &&
                    second.cachePath.StartsWith(workspace.LibraryPath, StringComparison.OrdinalIgnoreCase),
                "Per-instance cache escaped the project Library directory.");
            Require(Directory.Exists(first.scriptAssembliesPath) && Directory.Exists(second.scriptAssembliesPath),
                "Per-instance script assembly directories were not created.");

            Console.WriteLine("MULTI_INSTANCE_PROJECT_OK|instance-id,logs,temp,scripts,packages,shared-project");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"MULTI_INSTANCE_PROJECT_FAILED|{exception}");
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar).Equals(
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
