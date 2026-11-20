using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Platform;
namespace BEngine.Editor;

/// <summary>Resources that a module needs before game content and managed-code releases are available.</summary>
public static class RuntimeBuildResources
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<Assembly, RuntimeBuildResourceDirectory[]> Directories = [];
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register(string runtimeAssembly, params string[] directories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeAssembly);
        ArgumentNullException.ThrowIfNull(directories);
        foreach (var path in directories)
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.AsSpan().HasPathTraversal(includeCurrent: false))
                throw new ArgumentException("Runtime resource directories must remain inside the package Resources folder.", nameof(directories));
        lock (Gate) Directories[Assembly.GetCallingAssembly()] = (directories is { } projectionSource1005 ? projectionSource1005.Length == 0 ? global::System.Array.Empty<global::BEngine.Editor.RuntimeBuildResourceDirectory>() : global::System.Array.ConvertAll<string, global::BEngine.Editor.RuntimeBuildResourceDirectory>(projectionSource1005, path => new RuntimeBuildResourceDirectory(runtimeAssembly, path.Replace('\\', '/'))) : throw new global::System.ArgumentNullException("source"));
    }
    /// <summary>
    /// Enumerates the files requested by registered modules. The publishing workflow resolves each
    /// module's Resources directory and decides how to filter, store and load the resulting files.
    /// </summary>
    public static IEnumerable<RuntimeBuildResourceFile> EnumerateFiles(
        Func<string, string> resolveResourceRoot, FileUtility files, PathUtility paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolveResourceRoot);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(paths);
        return Enumerate(GetDirectories());

        IEnumerable<RuntimeBuildResourceFile> Enumerate(IReadOnlyList<RuntimeBuildResourceDirectory> contributions)
        {
            var seen = new HashSet<(string Assembly, string Path)>();
            var roots = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var contribution in contributions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!roots.TryGetValue(contribution.RuntimeAssembly, out var root))
                {
                    root = paths.GetFullPath(resolveResourceRoot(contribution.RuntimeAssembly));
                    roots.Add(contribution.RuntimeAssembly, root);
                }
                var directory = paths.GetFullPath(paths.Combine(root, contribution.RelativePath));
                var prefix = paths.TrimEndingDirectorySeparator(root) + paths.DirectorySeparatorChar;
                if (!directory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    !directory.Equals(paths.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Runtime module resources must remain inside their resource root.");
                if (!files.DirectoryExists(directory))
                    throw new DirectoryNotFoundException($"Runtime module resources are missing: '{directory}'.");
                foreach (var file in files.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = paths.GetRelativePath(root, file).Replace('\\', '/');
                    if (seen.Add((contribution.RuntimeAssembly, relative)))
                        yield return new(contribution.RuntimeAssembly, relative, file);
                }
            }
        }
    }
    public static IReadOnlyList<RuntimeBuildResourceDirectory> GetDirectories()
    {
        lock (Gate)
        {
            var count = 0;
            foreach (var items in Directories.Values) count += items.Length;
            if (count == 0) return [];
            var result = new RuntimeBuildResourceDirectory[count];
            var offset = 0;
            foreach (var items in Directories.Values)
            {
                items.CopyTo(result, offset);
                offset += items.Length;
            }
            return result;
        }
    }
    internal static void UnregisterAssembly(Assembly owner) { lock (Gate) Directories.Remove(owner); }
}
public readonly record struct RuntimeBuildResourceDirectory(string RuntimeAssembly, string RelativePath);
public readonly record struct RuntimeBuildResourceFile(string RuntimeAssembly, string RelativePath, string FullPath);
