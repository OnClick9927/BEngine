namespace BEngine.Serialization;

// A reference plus its last source snapshot. Comparing the snapshot with the saved
// hierarchy distinguishes inherited values from overrides, including deletions.
internal sealed class PrefabReferenceData
{
    public string Asset { get; set; } = string.Empty;
    public Guid Root { get; set; }
    public Dictionary<Guid, Guid> ObjectIds { get; set; } = [];
    public List<GameObjectData> Baseline { get; set; } = [];
}
