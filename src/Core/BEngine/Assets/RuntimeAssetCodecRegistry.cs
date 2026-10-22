using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine;

public static class RuntimeAssetCodecRegistry
{
    private static readonly Dictionary<Type, List<Registration>> Codecs = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register<TAsset>(Func<ReadOnlyMemory<byte>, string, TAsset> decoder)
        where TAsset : BAsset
    {
        ArgumentNullException.ThrowIfNull(decoder);
        var owner = decoder.Method.DeclaringType?.Assembly ?? Assembly.GetCallingAssembly();
        if (!Codecs.TryGetValue(typeof(TAsset), out var registrations))
            Codecs.Add(typeof(TAsset), registrations = []);
        registrations.RemoveAll(registration => registration.Owner == owner);
        registrations.Add(new Registration((bytes, path) => decoder(bytes, path), owner));
    }

    /// <summary>
    /// Registers optional pure-data preparation for asynchronous loads. Prepare
    /// must not create engine objects or access engine services; commit runs on
    /// the owning frame thread. Synchronous loads preserve their decoder path.
    /// IDisposable prepared payloads are disposed after commit or discard;
    /// commit must transfer ownership of any buffers it retains.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RegisterPrepared<TAsset, TPrepared>(
        Func<ReadOnlyMemory<byte>, string, TAsset> decoder,
        Func<ReadOnlyMemory<byte>, CancellationToken, TPrepared> prepare,
        Func<TPrepared, string, TAsset> commit) where TAsset : BAsset
    {
        ArgumentNullException.ThrowIfNull(decoder); ArgumentNullException.ThrowIfNull(prepare); ArgumentNullException.ThrowIfNull(commit);
        var owner = decoder.Method.DeclaringType?.Assembly ?? Assembly.GetCallingAssembly();
        if (!Codecs.TryGetValue(typeof(TAsset), out var registrations)) Codecs.Add(typeof(TAsset), registrations = []);
        registrations.RemoveAll(registration => registration.Owner == owner);
        registrations.Add(new Registration((bytes, path) => decoder(bytes, path), owner,
            (bytes, token) => prepare(bytes, token), (prepared, path) => commit((TPrepared)prepared!, path)));
    }

    internal static async BTask<BAsset?> DecodeAsync(ReadOnlyMemory<byte> bytes, string sourcePath,
        Type assetType, AsyncLoadContext context, Func<Func<BAsset>, BAsset>? identityGate = null)
    {
        if (!Codecs.TryGetValue(assetType, out var registrations) || registrations.Count == 0) return null;
        var registration = registrations[^1];
        BAsset? result = null;
        if (registration.Prepare is { } prepare && BackgroundWorkScheduler.IsSupported)
        {
            var prepared = await BackgroundWorkScheduler.Main.RunOwned(token => prepare(bytes, token), registration.Owner, context.cancellationToken,
                static value => (value as IDisposable)?.Dispose());
            try
            {
                context.cancellationToken.ThrowIfCancellationRequested();
                await context.RunSteps(() =>
                {
                    if (!Codecs.TryGetValue(assetType, out var current) || current.Count == 0 || !ReferenceEquals(current[^1], registration))
                        throw new InvalidOperationException("The asset codec changed while data was being prepared.");
                    result = identityGate is null ? registration.Commit!(prepared, sourcePath)
                        : identityGate(() => registration.Commit!(prepared, sourcePath));
                    return CooperativeWorkStatus.Complete;
                });
            }
            finally { (prepared as IDisposable)?.Dispose(); }
        }
        else await context.RunSteps(() =>
        {
            result = identityGate is null ? registration.Decoder(bytes, sourcePath)
                : identityGate(() => registration.Decoder(bytes, sourcePath));
            return CooperativeWorkStatus.Complete;
        });
        if (result is null || !assetType.IsInstanceOfType(result))
            throw new InvalidDataException($"Runtime asset decoder for {assetType.FullName} returned an invalid asset.");
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Unregister<TAsset>() where TAsset : BAsset
    {
        if (!Codecs.TryGetValue(typeof(TAsset), out var registrations)) return false;
        var caller = Assembly.GetCallingAssembly();
        var index = registrations.FindLastIndex(registration => registration.Owner == caller);
        if (index < 0) index = registrations.Count - 1;
        registrations.RemoveAt(index);
        if (registrations.Count == 0) Codecs.Remove(typeof(TAsset));
        return true;
    }

    internal static bool TryDecode(ReadOnlyMemory<byte> bytes, string sourcePath, Type assetType,
        out BAsset asset)
    {
        if (!Codecs.TryGetValue(assetType, out var registrations) || registrations.Count == 0)
        {
            asset = null!;
            return false;
        }
        var registration = registrations[^1];
        asset = registration.Decoder(bytes, sourcePath) ?? throw new InvalidDataException(
            $"Runtime asset decoder for {assetType.FullName} returned null.");
        if (!assetType.IsInstanceOfType(asset))
            throw new InvalidDataException(
                $"Runtime asset decoder returned {asset.GetType().FullName}, expected {assetType.FullName}.");
        return true;
    }

    internal static bool HasDecoder(Type assetType) =>
        Codecs.TryGetValue(assetType, out var registrations) && registrations.Count > 0;
    internal static bool HasPreparedDecoder(Type assetType) => Codecs.TryGetValue(assetType, out var registrations) &&
        registrations.Count > 0 && registrations[^1].Prepare is not null;

    internal static void UnregisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var (type, registrations) in Codecs.ToArray())
        {
            registrations.RemoveAll(registration => registration.Owner == assembly);
            if (registrations.Count == 0) Codecs.Remove(type);
        }
    }

    internal static bool HasRegistrationsFrom(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return Codecs.Values.Any(registrations =>
            registrations.Any(registration => registration.Owner == assembly));
    }

    private sealed record Registration(Func<ReadOnlyMemory<byte>, string, BAsset> Decoder, Assembly Owner,
        Func<ReadOnlyMemory<byte>, CancellationToken, object?>? Prepare = null,
        Func<object?, string, BAsset>? Commit = null);
}
