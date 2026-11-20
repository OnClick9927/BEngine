namespace BEngine.Serialization;

/// <summary>Opaque parsed scene input. Storage providers cannot mutate the engine document schema.</summary>
public readonly struct SceneLoadData
{
    internal readonly SceneAssetData? Document;
    internal SceneLoadData(SceneAssetData document) => Document = document;
}

/// <summary>Scene hydration shared by source, embedded and remote content providers.</summary>
public static class SceneAssetLoading
{
    public static Scene LoadSource(string path, IServiceProvider? services = null) =>
        SceneAssetSerialization.Load(path, services);

    public static SceneLoadData ReadData(ReadOnlySpan<byte> bytes) =>
        new(SceneRuntimeDataSerializer.Deserialize(bytes));

    public static Scene Restore(SceneLoadData data, IServiceProvider? services = null) =>
        SceneAssetSerialization.Restore(RequireDocument(data), services);

    /// <summary>Restores a scene, then lets component owners migrate references from this content source.
    /// No resource provider is globally registered or replaced.</summary>
    public static Scene Restore(SceneLoadData data, IServiceProvider? services, IResourceObjectProvider resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return SceneAssetSerialization.Restore(RequireDocument(data), services, resources: resources);
    }

    public static BTask<Scene> RestoreAsync(SceneLoadData data, IServiceProvider? services, AsyncLoadContext context) =>
        SceneAssetSerialization.RestoreAsync(RequireDocument(data), services, context);

    public static BTask<Scene> RestoreAsync(SceneLoadData data, IServiceProvider? services,
        AsyncLoadContext context, IResourceObjectProvider resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return SceneAssetSerialization.RestoreAsync(RequireDocument(data), services, context, resources: resources);
    }

    private static SceneAssetData RequireDocument(SceneLoadData data) => data.Document ??
        throw new ArgumentException("Scene input must be read before it can be restored.", nameof(data));
}
