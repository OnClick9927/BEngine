using System.Reflection;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace BEngine;

public static class RuntimeAssetCodecRegistry
{
    private static readonly Dictionary<Type, List<Registration>> Codecs = [];
    private static readonly ConcurrentDictionary<Type, (Func<string, BAsset?> Load, Assembly Owner)> FileLoaders = new();

    public static void RegisterFileLoader<TAsset>(Func<string, TAsset?> loader) where TAsset : BAsset
    {
        ArgumentNullException.ThrowIfNull(loader);
        FileLoaders[typeof(TAsset)] = (path => loader(path), loader.Method.DeclaringType!.Assembly);
    }

    internal static bool HasFileLoader(Type type) => FileLoaders.ContainsKey(type);
    internal static BAsset? LoadFile(string path, Type type) => FileLoaders.TryGetValue(type, out var loader) ? loader.Load(path) : null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register<TAsset>(Func<ReadOnlyMemory<byte>, string, TAsset> decoder)
        where TAsset : BAsset
    {
        ArgumentNullException.ThrowIfNull(decoder);
        var owner = decoder.Method.DeclaringType?.Assembly ?? Assembly.GetCallingAssembly();
        if (!Codecs.TryGetValue(typeof(TAsset), out var registrations))
            Codecs.Add(typeof(TAsset), registrations = []);
        for (var index = registrations.Count - 1; index >= 0; index--)
            if (registrations[index].Owner == owner) registrations.RemoveAt(index);
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
        for (var index = registrations.Count - 1; index >= 0; index--)
            if (registrations[index].Owner == owner) registrations.RemoveAt(index);
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
        var index = registrations.Count - 1;
        while (index >= 0 && registrations[index].Owner != caller) index--;
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
        RuntimeResourceCodecs.UnregisterAssembly(assembly);
        foreach (var pair in FileLoaders)
            if (pair.Value.Owner == assembly)
                FileLoaders.TryRemove(pair);
        foreach (var (type, registrations) in Codecs.ToArray())
        {
            for (var index = registrations.Count - 1; index >= 0; index--)
                if (registrations[index].Owner == assembly) registrations.RemoveAt(index);
            if (registrations.Count == 0) Codecs.Remove(type);
        }
    }

    internal static bool HasRegistrationsFrom(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var registrations in Codecs.Values)
            foreach (var registration in registrations)
                if (registration.Owner == assembly) return true;
        bool __queryResult7682;
        {
            var __querySource7682 = FileLoaders.Values;
            ArgumentNullException.ThrowIfNull(__querySource7682, "source");
            __queryResult7682 = false;
            var __querySource7682Type = __querySource7682.GetType();
            if (__querySource7682Type == typeof((System.Func<string, BEngine.BAsset?> Load, System.Reflection.Assembly Owner)[]) || __querySource7682Type == typeof(System.Collections.Generic.List<(System.Func<string, BEngine.BAsset?> Load, System.Reflection.Assembly Owner)>))
            {
                System.ReadOnlySpan<(System.Func<string, BEngine.BAsset?> Load, System.Reflection.Assembly Owner)> __querySource7682Span = __querySource7682Type == typeof((System.Func<string, BEngine.BAsset?> Load, System.Reflection.Assembly Owner)[]) ? ((System.Func<string, BEngine.BAsset?> Load, System.Reflection.Assembly Owner)[])(object)__querySource7682 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<(System.Func<string, BEngine.BAsset?> Load, System.Reflection.Assembly Owner)>)(object)__querySource7682);
                foreach (var __queryItem7682 in __querySource7682Span)
                {
                    if ((__queryItem7682.Owner == assembly))
                    {
                        __queryResult7682 = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var __queryItem7682 in __querySource7682)
                {
                    if ((__queryItem7682.Owner == assembly))
                    {
                        __queryResult7682 = true;
                        break;
                    }
                }
            }
        }
        
        if (__queryResult7682)
            return true;
        return false;
    }

    private sealed record Registration(Func<ReadOnlyMemory<byte>, string, BAsset> Decoder, Assembly Owner,
        Func<ReadOnlyMemory<byte>, CancellationToken, object?>? Prepare = null,
        Func<object?, string, BAsset>? Commit = null);
}
