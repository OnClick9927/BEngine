namespace BEngine.Serialization;

/// <summary>Copies execution data without JSON, text encoding, reflection or CLR type activation.</summary>
internal static class AssetSnapshotClone
{
    internal static SceneAssetData Clone(SceneAssetData source) => new()
    {
        Format = source.Format, Version = source.Version, Id = source.Id, Name = source.Name,
        GameObjects = Clone(source.GameObjects), PrefabInstances = source.PrefabInstances.Select(Clone).ToList(),
        IsRuntimeSnapshot = source.IsRuntimeSnapshot
    };
    internal static PrefabAssetData Clone(PrefabAssetData source) => new()
    {
        Format = source.Format, Version = source.Version, Id = source.Id, Name = source.Name, Root = source.Root,
        GameObjects = Clone(source.GameObjects), BasePrefab = source.BasePrefab is { } reference ? Clone(reference) : null,
        Instances = source.Instances.Select(Clone).ToList()
    };
    internal static PrefabReferenceData Clone(PrefabReferenceData source) => new()
    {
        Asset = source.Asset, Root = source.Root, ObjectIds = new(source.ObjectIds), Baseline = Clone(source.Baseline)
    };
    internal static List<GameObjectData> Clone(List<GameObjectData> source) => source.Select(Clone).ToList();
    internal static List<PrefabReferenceData> Clone(List<PrefabReferenceData> source) => source.Select(Clone).ToList();
    internal static GameObjectData Clone(GameObjectData source) => new()
    {
        Id = source.Id, Name = source.Name, Active = source.Active, Tag = source.Tag, Layer = source.Layer,
        IsStatic = source.IsStatic, Parent = source.Parent, PrefabAsset = source.PrefabAsset, PrefabSource = source.PrefabSource,
        Transform = new()
        {
            Id = source.Transform.Id, Type = source.Transform.Type, PrefabAsset = source.Transform.PrefabAsset,
            PrefabSource = source.Transform.PrefabSource,
            LocalPosition = new() { X = source.Transform.LocalPosition.X, Y = source.Transform.LocalPosition.Y }, LocalRotation = source.Transform.LocalRotation,
            LocalScale = new() { X = source.Transform.LocalScale.X, Y = source.Transform.LocalScale.Y }, Fields = new(source.Transform.Fields, StringComparer.Ordinal),
            Graph = Clone(source.Transform.Graph)
        },
        Components = source.Components.Select(static component => new ComponentData
        {
            Id = component.Id, Type = component.Type, Enabled = component.Enabled, PrefabAsset = component.PrefabAsset,
            PrefabSource = component.PrefabSource, Fields = new(component.Fields, StringComparer.Ordinal), Graph = Clone(component.Graph)
        }).ToList()
    };
    private static ComponentGraphData? Clone(ComponentGraphData? graph) =>
        graph is null ? null : ComponentGraphClone.Clone(graph);
}
