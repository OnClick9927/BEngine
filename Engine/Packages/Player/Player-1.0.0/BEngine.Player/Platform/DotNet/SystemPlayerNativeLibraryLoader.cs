using System.Runtime.InteropServices;
using System.Reflection;

namespace BEngine.Player;

public sealed class SystemPlayerNativeLibraryLoader : IPlayerNativeLibraryLoader
{
    private static readonly List<nint> NativeLibraryHandles = [];
    private static bool _nativeLibrariesInitialized;
    private static string[] _nativeDirectories = [];
    private static readonly Dictionary<string, nint> ResolvedImports = new(StringComparer.Ordinal);

    public void InitializeDesktopLibraries(string nativeLibraryDirectory)
    {
        if (_nativeLibrariesInitialized ||
            !(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())) return;
        _nativeLibrariesInitialized = true;
        var directories = EnumerateNativeLibraryDirectories(nativeLibraryDirectory)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _nativeDirectories = directories;
        if (OperatingSystem.IsWindows() && directories.Length != 0)
        {
            var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator,
                directories.Append(current)));
        }

        var glfwName = OperatingSystem.IsWindows()
            ? "glfw3.dll"
            : OperatingSystem.IsMacOS() ? "libglfw.3.dylib" : "libglfw.so.3";
        string? loadedPath = null;
        foreach (var directory in directories)
        {
            var candidate = Path.Combine(directory, glfwName);
            if (!File.Exists(candidate) || !NativeLibrary.TryLoad(candidate, out var handle)) continue;
            NativeLibraryHandles.Add(handle);
            loadedPath = candidate;
            break;
        }
        if (Environment.GetEnvironmentVariable("BENGINE_PLAYER_NATIVE_DIAGNOSTICS") == "1")
            Console.Error.WriteLine(
                $"BENGINE_NATIVE_DIAGNOSTICS|base={AppContext.BaseDirectory}|" +
                $"runtime={AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES")}|" +
                $"directories={string.Join(',', directories)}|glfw={loadedPath ?? "not-loaded"}");
    }

    /// <summary>Resolves native imports from the lowercase packaged assembly directory.</summary>
    public static nint ResolveDesktopImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (Path.GetFileName(libraryName) != libraryName) return 0;
        var extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        var name = Path.HasExtension(libraryName) ? libraryName : libraryName + extension;
        var names = new[] { name, name.ToLowerInvariant() }.Distinct(StringComparer.Ordinal);
        lock (ResolvedImports)
        {
            foreach (var directory in _nativeDirectories.Concat(EnumerateNativeLibraryDirectories(string.Empty)).Distinct(StringComparer.Ordinal))
            foreach (var candidate in names)
            {
                var path = Path.Combine(directory, candidate);
                if (ResolvedImports.TryGetValue(path, out var loaded)) return loaded;
                if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var handle)) continue;
                ResolvedImports.Add(path, handle);
                NativeLibraryHandles.Add(handle); // Host imports live for the Player process lifetime.
                return handle;
            }
        }
        return 0;
    }

    private static IEnumerable<string> EnumerateNativeLibraryDirectories(string nativeLibraryDirectory)
    {
        if (!string.IsNullOrWhiteSpace(nativeLibraryDirectory))
            yield return Path.GetFullPath(nativeLibraryDirectory);
        if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string nativeDirectories)
            foreach (var directory in nativeDirectories.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return directory;
        yield return AppContext.BaseDirectory;
        yield return Path.Combine(
            AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
    }
}
