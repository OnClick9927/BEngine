using System.Reflection;

namespace BEngine;

public static class RuntimeAssetCodecRegistry
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<Type, Registration> Codecs = [];

    public static void Register<TAsset>(Func<ReadOnlyMemory<byte>, string, TAsset> decoder)
        where TAsset : BAsset
    {
        ArgumentNullException.ThrowIfNull(decoder);
        lock (Gate)
            Codecs[typeof(TAsset)] = new Registration(
                (bytes, path) => decoder(bytes, path), decoder.Method.DeclaringType?.Assembly ??
                                                       Assembly.GetCallingAssembly());
    }

    public static bool Unregister<TAsset>() where TAsset : BAsset
    {
        lock (Gate) return Codecs.Remove(typeof(TAsset));
    }

    internal static bool TryDecode(ReadOnlyMemory<byte> bytes, string sourcePath, Type assetType,
        out BAsset asset)
    {
        Registration? registration;
        lock (Gate) registration = Codecs.GetValueOrDefault(assetType);
        if (registration is null)
        {
            asset = null!;
            return false;
        }
        asset = registration.Decoder(bytes, sourcePath) ?? throw new InvalidDataException(
            $"Runtime asset decoder for {assetType.FullName} returned null.");
        if (!assetType.IsInstanceOfType(asset))
            throw new InvalidDataException(
                $"Runtime asset decoder returned {asset.GetType().FullName}, expected {assetType.FullName}.");
        return true;
    }

    internal static void UnregisterAssembly(Assembly assembly)
    {
        lock (Gate)
            foreach (var type in Codecs.Where(pair => pair.Value.Owner == assembly)
                         .Select(static pair => pair.Key).ToArray())
                Codecs.Remove(type);
    }

    private sealed record Registration(Func<ReadOnlyMemory<byte>, string, BAsset> Decoder, Assembly Owner);
}
