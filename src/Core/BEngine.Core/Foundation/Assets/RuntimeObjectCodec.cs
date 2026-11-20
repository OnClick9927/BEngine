using System.Reflection;
using System.Collections.Concurrent;

namespace BEngine;

/// <summary>Package-owned persistence and cloning for engine objects outside the core object model.</summary>
public abstract class RuntimeObjectCodec
{
    public abstract Type ObjectType { get; }
    public abstract BObject? ReadReference(string reference, IReadOnlyDictionary<string, string>? legacyFields = null);
    public abstract string WriteReference(BObject value);
    public virtual void ClearReferences() { }
    public virtual BObject Clone(BObject value) =>
        throw new NotSupportedException($"Instantiate does not support {value.GetType().FullName}.");

    /// <summary>Constructs an empty instance for editor scene isolation. Core copies
    /// serializable members and the object's private clone payload afterwards.</summary>
    /// <remarks>Must return a fresh instance. Ordinary Instantiate retains its own semantics.</remarks>
    public virtual BObject CreateCloneInstance() =>
        RuntimeTypeCache.GetFactory(ObjectType)?.Invoke() as BObject ??
        throw new NotSupportedException($"Cannot construct an isolated {ObjectType.FullName}.");
}

/// <summary>Registrations are owned by their defining assembly and removed when it is unloaded.</summary>
public static class RuntimeObjectCodecs
{
    private static readonly ConcurrentDictionary<Type, RuntimeObjectCodec> Codecs = new();
    public static void Register(RuntimeObjectCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        if (!typeof(BObject).IsAssignableFrom(codec.ObjectType))
            throw new ArgumentException("Object codecs must describe an engine object type.", nameof(codec));
        Codecs[codec.ObjectType] = codec;
    }
    public static RuntimeObjectCodec? Find(Type type) => Codecs.GetValueOrDefault(type);
    internal static void ClearReferences()
    {
        foreach (var codec in Codecs.Values) codec.ClearReferences();
    }
    public static bool IsReferenceType(Type type) => typeof(BAsset).IsAssignableFrom(type) || Codecs.ContainsKey(type);
    public static BObject? ReadReference(Type type, string reference, IReadOnlyDictionary<string, string>? legacyFields = null) =>
        string.IsNullOrWhiteSpace(reference) ? null : Find(type) is { } codec
            ? codec.ReadReference(reference, legacyFields) : BAssetReferenceLoader.LoadObjectReference(reference, type);
    public static void UnregisterAssembly(Assembly assembly)
    {
        foreach (var pair in Codecs)
            if (pair.Value.GetType().Assembly == assembly)
                Codecs.TryRemove(pair);
    }
}
