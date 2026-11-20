using System.Reflection;

namespace BEngine;

/// <summary>Storage-neutral identity and import metadata for one resource payload.</summary>
public readonly record struct RuntimeResourceMetadata(
    string Address, string SourcePathPrefix, string OwnerAddress, Guid Guid, Guid OwnerGuid,
    long LocalIdentifier, string AssetType, string Importer, IReadOnlyDictionary<string, string> Settings,
    bool IsSynthetic = false)
{
    /// <summary>Only formats that retain a source reference need to construct this path.</summary>
    public string SourcePath => SourcePathPrefix.Length == 0 ? Address : string.Concat(SourcePathPrefix, Address);
}

/// <summary>Object-format policy supplied by the module that owns the object type.</summary>
public abstract class RuntimeResourceCodec
{
    public abstract Type ObjectType { get; }
    /// <summary>Optional stable child identity backed by its owner's payload.</summary>
    public virtual long SyntheticLocalIdentifier => 0;
    /// <summary>Preserves byte-codec overrides, including their asynchronous preparation, for ordinary assets.</summary>
    public virtual bool PreferAssetDecoder => false;
    public abstract bool CanDecode(in RuntimeResourceMetadata metadata);
    public abstract BObject Decode(ReadOnlyMemory<byte> bytes, in RuntimeResourceMetadata metadata);
}

/// <summary>Engine-thread registry. Storage providers never need to know concrete resource types.</summary>
public static class RuntimeResourceCodecs
{
    private static readonly Dictionary<Type, List<RuntimeResourceCodec>> Codecs = [];
    private static readonly Dictionary<long, List<RuntimeResourceCodec>> SyntheticCodecs = [];

    public static void Register(RuntimeResourceCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        if (!typeof(BObject).IsAssignableFrom(codec.ObjectType) || codec.ObjectType.IsAbstract || codec.SyntheticLocalIdentifier < 0)
            throw new ArgumentException("A concrete engine object type and a non-negative child identity are required.", nameof(codec));
        if (!Codecs.TryGetValue(codec.ObjectType, out var registrations)) Codecs.Add(codec.ObjectType, registrations = []);
        var owner = codec.GetType().Assembly;
        for (var index = registrations.Count - 1; index >= 0; index--)
            if (registrations[index].GetType().Assembly == owner)
            { RemoveSynthetic(registrations[index]); registrations.RemoveAt(index); }
        registrations.Add(codec);
        if (codec.SyntheticLocalIdentifier > 0)
        {
            if (!SyntheticCodecs.TryGetValue(codec.SyntheticLocalIdentifier, out var children))
                SyntheticCodecs.Add(codec.SyntheticLocalIdentifier, children = []);
            children.Add(codec);
        }
    }

    public static RuntimeResourceCodec? Find(Type objectType) =>
        Codecs.TryGetValue(objectType, out var entries) && entries.Count > 0 ? entries[^1] : null;

    public static bool CanDecodeSynthetic(in RuntimeResourceMetadata metadata)
    {
        if (!metadata.IsSynthetic || !SyntheticCodecs.TryGetValue(metadata.LocalIdentifier, out var codecs)) return false;
        foreach (var codec in codecs)
            if (ReferenceEquals(Find(codec.ObjectType), codec) && codec.CanDecode(metadata)) return true;
        return false;
    }

    public static void UnregisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var (type, entries) in Codecs.ToArray())
        {
            for (var index = entries.Count - 1; index >= 0; index--)
                if (entries[index].GetType().Assembly == assembly)
                { RemoveSynthetic(entries[index]); entries.RemoveAt(index); }
            if (entries.Count == 0) Codecs.Remove(type);
        }
    }

    private static void RemoveSynthetic(RuntimeResourceCodec codec)
    {
        if (!SyntheticCodecs.TryGetValue(codec.SyntheticLocalIdentifier, out var entries)) return;
        entries.Remove(codec);
        if (entries.Count == 0) SyntheticCodecs.Remove(codec.SyntheticLocalIdentifier);
    }
}
