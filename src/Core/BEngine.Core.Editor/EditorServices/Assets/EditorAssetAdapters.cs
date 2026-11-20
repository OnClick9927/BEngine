using System.Reflection;
using System.Runtime.CompilerServices;
namespace BEngine.Editor;

/// <summary>Imported-object behavior contributed by the package that owns its asset format.</summary>
public abstract class EditorAssetAdapter
{
    public abstract Type AssetType { get; }
    public virtual bool ReadOnly => true;
    public virtual bool MayHaveRepresentations => false;
    public virtual IEditorAssetInspector? Inspector => null;
    public virtual IEditorAssetPreview? Preview => null;
    public virtual BObject? GetRepresentation(BObject asset, Type requestedType) => null;
    public virtual IEnumerable<BObject> GetRepresentations(BObject asset) => [];
    public virtual void Invalidate() { }
}

public static class EditorAssetAdapters
{
    private sealed record Registration(EditorAssetAdapter Adapter, Assembly Owner, IEditorAssetInspector? Inspector, IEditorAssetPreview? Preview);
    private static readonly Dictionary<Type, List<Registration>> Adapters = [];
    private static readonly Dictionary<Type, IEditorAssetInspector?> Inspectors = [];
    private static readonly Dictionary<Type, IEditorAssetPreview[]> Previews = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register(EditorAssetAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (!Adapters.TryGetValue(adapter.AssetType, out var items)) Adapters.Add(adapter.AssetType, items = []);
        // Preserve adapter replacement semantics; retain the caller as well as the implementation owner.
        items.RemoveAll(item => item.Adapter.GetType().Assembly == adapter.GetType().Assembly);
        items.Add(new Registration(adapter, Assembly.GetCallingAssembly(), adapter.Inspector, adapter.Preview));
        Inspectors.Clear();
        Previews.Clear();
    }
    public static bool IsReadOnly(BObject asset) => Adapters.TryGetValue(asset.GetType(), out var items) && items.Any(item => item.Adapter.ReadOnly);
    public static bool MayHaveRepresentations(Type type) => Adapters.TryGetValue(type, out var items) && items.Any(item => item.Adapter.MayHaveRepresentations);
    public static IEnumerable<BObject> GetRepresentations(BObject asset) => Adapters.TryGetValue(asset.GetType(), out var items)
        ? items.SelectMany(item => item.Adapter.GetRepresentations(asset)) : [];
    public static BObject? GetRepresentation(BObject asset, Type type) => GetRepresentations(asset).FirstOrDefault(type.IsInstanceOfType);

    /// <summary>Finds the most specific presentation. Other adapter behavior remains exact-type based.</summary>
    public static IEditorAssetInspector? GetInspector(BAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var type = asset.GetType();
        if (Inspectors.TryGetValue(type, out var cached)) return cached;
        for (var candidate = type; candidate is not null; candidate = candidate.BaseType)
        {
            if (!Adapters.TryGetValue(candidate, out var items)) continue;
            for (var i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].Inspector is not { } inspector) continue;
                Inspectors.Add(type, inspector);
                return inspector;
            }
        }
        Inspectors.Add(type, null);
        return null;
    }
    public static void Invalidate()
    {
        foreach (var items in Adapters.Values) foreach (var item in items) item.Adapter.Invalidate();
    }

    /// <summary>Uses cached type candidates; source-dependent eligibility is evaluated for each asset.</summary>
    public static IEditorAssetPreview? GetPreview(BAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var type = asset.GetType();
        if (!Previews.TryGetValue(type, out var candidates))
        {
            var matches = new List<IEditorAssetPreview>();
            for (var candidate = type; candidate is not null; candidate = candidate.BaseType)
            {
                if (!Adapters.TryGetValue(candidate, out var items)) continue;
                for (var i = items.Count - 1; i >= 0; i--)
                    if (items[i].Preview is { } preview) matches.Add(preview);
            }
            Previews.Add(type, candidates = matches.ToArray());
        }
        foreach (var candidate in candidates)
            if (candidate.CanPreview(asset)) return candidate;
        return null;
    }
    public static void UnregisterAssembly(Assembly assembly)
    {
        foreach (var type in Adapters.Keys.ToArray())
        {
            Adapters[type].RemoveAll(item => item.Owner == assembly || item.Adapter.GetType().Assembly == assembly ||
                type.Assembly == assembly || item.Inspector?.GetType().Assembly == assembly ||
                item.Preview?.GetType().Assembly == assembly);
            if (Adapters[type].Count == 0) Adapters.Remove(type);
        }
        // Also release cached derived asset types supplied by the retiring assembly.
        Inspectors.Clear();
        Previews.Clear();
    }
}
