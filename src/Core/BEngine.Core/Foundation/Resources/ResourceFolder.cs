namespace BEngine;

/// <summary>A named resource search folder using the same providers, roots and codecs as Resources.
/// It owns no provider registrations, cached objects or resource leases.</summary>
public sealed class ResourceFolder
{
    public string Name { get; }

    public ResourceFolder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is "." or ".." || name.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new ArgumentException("A resource folder name must be one relative path segment.", nameof(name));
        Name = name;
    }

    public T? Load<T>(string path) where T : class => ResourceLoader.Load<T>(path, Name);

    public object? Load(string path, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return ResourceLoader.Load(path, type, Name);
    }

    public string? FindPath(string path) => ResourceLoader.Resolve(path, Name);
}
