using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

/// <summary>Text formats whose contents can contribute references during Player builds.</summary>
public static class PlayerResourceTextFormats
{
    private static readonly Lock Gate = new();
    private static readonly string[] DocumentExtensions = [".yaml", ".yml", ".json", ".xml", ".txt"];
    private static readonly Dictionary<Assembly, Contribution> Contributions = [];
    private sealed record Contribution(string[] Extensions, Func<string, string, IEnumerable<string>>? ReadTypeNames);
    private sealed record Snapshot(FrozenSet<string> Extensions, Contribution[] Readers);
    private static Snapshot _snapshot = new(DocumentExtensions.ToFrozenSet(StringComparer.OrdinalIgnoreCase), []);

    static PlayerResourceTextFormats() => RuntimeAssemblyCleanup.Register(UnregisterAssembly);

    /// <summary>Replaces the calling assembly's format contribution. Unloading it removes that contribution.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register(params string[] extensions) => Register(Assembly.GetCallingAssembly(), null, extensions);

    /// <summary>Also contributes format-specific type names without loading the runtime objects.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register(Func<string, string, IEnumerable<string>> readTypeNames, params string[] extensions)
    {
        ArgumentNullException.ThrowIfNull(readTypeNames);
        Register(Assembly.GetCallingAssembly(), readTypeNames, extensions);
    }

    private static void Register(Assembly owner, Func<string, string, IEnumerable<string>>? readTypeNames, string[] extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        foreach (var extension in extensions)
            if (string.IsNullOrWhiteSpace(extension) || extension.Length < 2 || extension[0] != '.' ||
                extension.AsSpan(1).IndexOfAny('.', '/', '\\') >= 0 || extension.Any(char.IsWhiteSpace))
                throw new ArgumentException("A text format must be a single file extension beginning with '.'.", nameof(extensions));
        lock (Gate)
        {
            Contributions[owner] = new(extensions.ToArray(), readTypeNames);
            Rebuild();
        }
    }

    public static bool IsTextFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Volatile.Read(ref _snapshot).Extensions.Contains(Path.GetExtension(path));
    }

    public static IEnumerable<string> GetReferencedTypeNames(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);
        var snapshot = Volatile.Read(ref _snapshot);
        var extension = Path.GetExtension(path);
        return snapshot.Readers.Where(reader => reader.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            .SelectMany(reader => reader.ReadTypeNames!(path, text));
    }

    private static void UnregisterAssembly(Assembly owner)
    {
        lock (Gate)
            if (Contributions.Remove(owner)) Rebuild();
    }

    private static void Rebuild() => Volatile.Write(ref _snapshot, new(
        DocumentExtensions.Concat(Contributions.Values.SelectMany(static contribution => contribution.Extensions))
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase),
        Contributions.Values.Where(static contribution => contribution.ReadTypeNames is not null).ToArray()));
}
