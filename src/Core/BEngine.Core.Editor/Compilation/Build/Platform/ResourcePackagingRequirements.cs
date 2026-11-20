namespace BEngine.Editor;

/// <summary>Content-format requirements consumed by platform publishers.</summary>
public sealed class ResourcePackagingRequirements
{
    public static ResourcePackagingRequirements Empty { get; } = new([]);

    private ResourcePackagingRequirements(string[] extensions) =>
        UncompressedFileExtensions = Array.AsReadOnly(extensions);

    /// <summary>File extensions which must remain seekable inside a platform container.</summary>
    public IReadOnlyList<string> UncompressedFileExtensions { get; }

    /// <summary>Creates a normalized, immutable snapshot without prescribing a content format.</summary>
    public static ResourcePackagingRequirements Create(IEnumerable<string> uncompressedFileExtensions)
    {
        ArgumentNullException.ThrowIfNull(uncompressedFileExtensions);
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in uncompressedFileExtensions)
        {
            if (string.IsNullOrEmpty(extension) || extension.Length < 2 || extension[0] != '.' ||
                !char.IsAsciiLetterOrDigit(extension[1]) || extension[^1] == '.' ||
                extension.Contains("..", StringComparison.Ordinal) ||
                extension.AsSpan(1).ContainsAnyExcept(
                    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-"))
                throw new ArgumentException("Resource extensions must begin with a dot and contain only " +
                    "ASCII letters, digits, dots, underscores or hyphens.", nameof(uncompressedFileExtensions));
            extensions.Add(extension.ToLowerInvariant());
        }
        if (extensions.Count == 0) return Empty;
        var snapshot = extensions.ToArray();
        Array.Sort(snapshot, StringComparer.Ordinal);
        return new ResourcePackagingRequirements(snapshot);
    }
}
