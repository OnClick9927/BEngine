using System.Reflection;
using System.Runtime.InteropServices;
using BEngine.Hosting;

namespace BEngine.Platform;

/// <summary>Configures host-selected search paths and resolves process-lifetime native imports.</summary>
public class SystemNativeLibraryLoader : INativeLibraryLoader
{
    private static readonly Dictionary<string, nint> ResolvedImports = new(StringComparer.Ordinal);
    private static bool _initialized;
    private static string[] _nativeDirectories = [];

    public virtual void Initialize(string nativeLibraryDirectory, bool reportDiagnostics = false)
    {
        if (!(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())) return;
        lock (ResolvedImports)
        {
            if (!_initialized)
            {
                var directories = ((((global::System.Collections.Generic.IEnumerable<string>)(EnumerateNativeLibraryDirectories(nativeLibraryDirectory).Where(Directory.Exists))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.OrdinalIgnoreCase)) switch
                {
                    (string[] { Length: 0 }, _) => global::System.Array.Empty<string>(),
                    var (distinctItems813, distinctComparer813) => distinctItems813.ToHashSet<string>(distinctComparer813).ToArray()});
                _nativeDirectories = directories;
                if (OperatingSystem.IsWindows() && directories.Length != 0)
                {
                    var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                    Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, directories.Append(current)));
                }
                _initialized = true;
            }
        }
        if (reportDiagnostics) ReportDiagnostics();
    }

    public nint ResolveImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (Path.GetFileName(libraryName) != libraryName) return 0;
        var extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        var name = Path.HasExtension(libraryName) ? libraryName : libraryName + extension;
        return LoadFromSearchDirectories(new[] { name, name.ToLowerInvariant() }.Distinct(StringComparer.Ordinal), out _);
    }

    protected static nint LoadFromSearchDirectories(IEnumerable<string> names, out string? loadedPath)
    {
        lock (ResolvedImports)
        {
            foreach (var directory in _nativeDirectories.Concat(EnumerateNativeLibraryDirectories(string.Empty)).Distinct(StringComparer.Ordinal))
            foreach (var name in names)
            {
                var path = Path.Combine(directory, name);
                if (ResolvedImports.TryGetValue(path, out var loaded)) { loadedPath = path; return loaded; }
                if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var handle)) continue;
                ResolvedImports.Add(path, handle); // Resolvers own these imports for the process lifetime.
                loadedPath = path;
                return handle;
            }
        }
        loadedPath = null;
        return 0;
    }

    protected static void ReportDiagnostics(string? suffix = null) => Console.Error.WriteLine(
        $"BENGINE_NATIVE_DIAGNOSTICS|base={AppContext.BaseDirectory}|" +
        $"runtime={AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES")}|" +
        $"directories={string.Join(',', _nativeDirectories)}{suffix}");

    private static IEnumerable<string> EnumerateNativeLibraryDirectories(string nativeLibraryDirectory)
    {
        if (!string.IsNullOrWhiteSpace(nativeLibraryDirectory)) yield return Path.GetFullPath(nativeLibraryDirectory);
        if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string nativeDirectories)
            foreach (var directory in nativeDirectories.Split(Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return directory;
        yield return AppContext.BaseDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
    }
}
