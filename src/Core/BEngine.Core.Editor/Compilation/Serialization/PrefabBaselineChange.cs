using BEngine.Serialization;

namespace BEngine.Editor;

/// <summary>A prepared edit to a live prefab reference's inherited snapshot. Detached callback
/// data is copied before publication. The owner of a larger transaction controls apply/rollback.</summary>
public sealed class PrefabBaselineChange
{
    private readonly PrefabReferenceData _reference;
    private readonly List<GameObjectData> _previous;
    private readonly List<GameObjectData> _next;
    private bool _applied;

    private PrefabBaselineChange(PrefabReferenceData reference, List<GameObjectData> previous, List<GameObjectData> next)
    {
        _reference = reference;
        _previous = previous;
        _next = next;
    }

    /// <summary>Prepares each distinct connected reference once. Returning false discards that
    /// detached edit. Throws before publishing any baseline if a callback fails.</summary>
    public static IReadOnlyList<PrefabBaselineChange> Prepare(IEnumerable<GameObject> objects,
        Func<SerializedHierarchy, bool> edit)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(edit);
        var result = new List<PrefabBaselineChange>();
        var visited = new HashSet<PrefabReferenceData>(ReferenceEqualityComparer.Instance);
        foreach (var item in objects)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.PrefabInstance is not { } reference || !visited.Add(reference)) continue;
            var previous = reference.Baseline;
            var detached = new SerializedHierarchy(AssetSnapshotClone.Clone(previous));
            var publish = edit(detached);
            if (!ReferenceEquals(reference.Baseline, previous))
                throw new InvalidOperationException("The prefab baseline changed while its edit was being prepared.");
            if (publish) result.Add(new(reference, previous, AssetSnapshotClone.Clone(detached.Data)));
        }
        return result.AsReadOnly();
    }

    public void Apply()
    {
        if (_applied)
        {
            RequireCurrent(_next);
            return;
        }
        RequireCurrent(_previous);
        _reference.Baseline = _next;
        _applied = true;
    }

    public void Rollback()
    {
        if (!_applied) return;
        RequireCurrent(_next);
        _reference.Baseline = _previous;
        _applied = false;
    }

    private void RequireCurrent(List<GameObjectData> expected)
    {
        if (!ReferenceEquals(_reference.Baseline, expected))
            throw new InvalidOperationException("The prefab baseline changed after this edit was prepared or applied.");
    }
}
