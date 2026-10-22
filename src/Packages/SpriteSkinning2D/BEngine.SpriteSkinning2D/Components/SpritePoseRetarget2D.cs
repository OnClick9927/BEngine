namespace BEngine;

public sealed class SpriteBoneRetargetMapping
{
    public Transform? Source { get; set; }
    public Transform? Target { get; set; }
    public Vector2 SourcePosition { get; set; }
    public Vector2 TargetPosition { get; set; }
    public Vector2 SourceScale { get; set; } = Vector2.one;
    public Vector2 TargetScale { get; set; } = Vector2.one;
    public Fix64 SourceRotation { get; set; }
    public Fix64 TargetRotation { get; set; }
    public Fix64 TranslationScale { get; set; } = Fix64.One;
    public bool HasReferencePose { get; set; }
}

/// <summary>Transfers local pose deltas between two authored reference poses without changing either skin asset.</summary>
[AddComponentMenu("2D Animation/Pose Retarget 2D")]
public sealed class SpritePoseRetarget2D : MonoBehaviour
{
    public Transform? sourceRoot { get; set; }
    public Transform? targetRoot { get; set; }
    public List<SpriteBoneRetargetMapping> mappings { get; set; } = [];
    public bool retargetRotation { get; set; } = true;
    public bool retargetPosition { get; set; }
    public bool retargetScale { get; set; }
    [Range(0, 1)] public Fix64 weight { get; set; } = Fix64.One;

    public int MapByName()
    {
        if (sourceRoot is null || targetRoot is null) return 0;
        var source = Descendants(sourceRoot).GroupBy(bone => bone.gameObject.name, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        mappings = Descendants(targetRoot).Where(bone => source.ContainsKey(bone.gameObject.name))
            .Select(bone => new SpriteBoneRetargetMapping { Source = source[bone.gameObject.name], Target = bone }).ToList();
        return mappings.Count;
    }

    public int CaptureReferencePose()
    {
        var captured = 0;
        foreach (var map in mappings)
        {
            if (map.Source is not { isDestroyed: false } source || map.Target is not { isDestroyed: false } target || ReferenceEquals(source, target)) continue;
            if (source.localScale.x == 0 || source.localScale.y == 0) continue;
            map.SourcePosition = source.localPosition; map.TargetPosition = target.localPosition;
            map.SourceRotation = source.localRotation; map.TargetRotation = target.localRotation;
            map.SourceScale = source.localScale; map.TargetScale = target.localScale;
            map.HasReferencePose = true; captured++;
        }
        return captured;
    }

    public override void LateUpdate() => Apply();

    public bool Apply()
    {
        if (!enabled || weight <= 0) return false;
        var blend = Mathf.Clamp01(weight); var applied = false;
        // Snapshot every source before writing targets so chained mappings are independent of list order.
        var poses = mappings.Select(map => map.Source is { isDestroyed: false } source
            ? (Valid: true, Position: source.localPosition, Rotation: source.localRotation, Scale: source.localScale)
            : (Valid: false, Position: Vector2.zero, Rotation: Fix64.Zero, Scale: Vector2.one)).ToArray();
        for (var index = 0; index < mappings.Count; index++)
        {
            var map = mappings[index]; var pose = poses[index];
            if (!pose.Valid || !map.HasReferencePose || map.Target is not { isDestroyed: false } target ||
                map.SourceScale.x == 0 || map.SourceScale.y == 0) continue;
            if (retargetPosition)
                target.localPosition = map.TargetPosition + (pose.Position - map.SourcePosition) * map.TranslationScale * blend;
            if (retargetRotation)
            {
                var delta = (pose.Rotation - map.SourceRotation) % 360;
                if (delta > 180) delta -= 360; else if (delta < -180) delta += 360;
                target.localRotation = map.TargetRotation + delta * blend;
            }
            if (retargetScale)
            {
                var ratio = new Vector2(pose.Scale.x / map.SourceScale.x, pose.Scale.y / map.SourceScale.y);
                target.localScale = new(map.TargetScale.x * (1 + (ratio.x - 1) * blend), map.TargetScale.y * (1 + (ratio.y - 1) * blend));
            }
            applied = true;
        }
        return applied;
    }

    private static IEnumerable<Transform> Descendants(Transform root)
    {
        yield return root;
        foreach (var child in root.children)
        foreach (var bone in Descendants(child)) yield return bone;
    }
}
