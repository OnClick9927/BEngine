using BEngine.Serialization;

namespace BEngine;

public enum AnimatorBlendTreeType { Simple1D, Cartesian2D }

/// <summary>A parameter-driven blend of clips or nested trees.</summary>
public sealed class AnimatorBlendTree
{
    public AnimatorBlendTreeType blendType { get; set; }
    public string parameter { get; set; } = string.Empty;
    public string parameterY { get; set; } = string.Empty;
    public List<AnimatorBlendMotion> motions { get; set; } = [];

    private readonly ResolveWorkspace _workspace = new();
    private AnimatorBlendMotion[] _orderedCache = [];
    private int _orderedCount;
    private MotionSnapshot[] _motionSnapshots = [];
    private int _snapshotCount = -1;
    private CartesianWorkspace? _cartesian;

    internal IReadOnlyList<(AnimationClip Clip, Fix64 Weight)> Resolve(Fix64 value) => Resolve(_ => value);

    internal IReadOnlyList<(AnimationClip Clip, Fix64 Weight)> Resolve(Func<string, Fix64> readParameter)
    {
        var workspace = _workspace;
        workspace.Result.Clear(); workspace.Output.Clear(); workspace.Path.Clear(); workspace.Visited = 0;
        Collect(this, Fix64.One, 0);
        var total = Fix64.Zero;
        foreach (var weight in workspace.Result.Values) total += weight;
        if (total <= 0) return Array.Empty<(AnimationClip Clip, Fix64 Weight)>();
        foreach (var pair in workspace.Result) workspace.Output.Add((pair.Key, pair.Value / total));
        return workspace.Output;

        void Collect(AnimatorBlendTree tree, Fix64 parentWeight, int depth)
        {
            Guard(tree, workspace.Path, depth, ref workspace.Visited);
            try
            {
                var ordered = tree.GetOrderedMotions(out var orderedCount);
                if (tree.blendType == AnimatorBlendTreeType.Cartesian2D)
                    tree.CollectCartesian(ordered, orderedCount, readParameter(tree.parameter), readParameter(tree.parameterY), parentWeight, Collect, workspace);
                else
                {
                    var value = readParameter(tree.parameter);
                    if (orderedCount == 0) { }
                    else if (orderedCount == 1 || value <= ordered[0].threshold) Add(ordered[0], parentWeight);
                    else if (value >= ordered[orderedCount - 1].threshold) Add(ordered[orderedCount - 1], parentWeight);
                    else
                    {
                        for (var index = 1; index < orderedCount; index++)
                        {
                            var right = ordered[index]; if (value > right.threshold) continue;
                            var left = ordered[index - 1]; var range = right.threshold - left.threshold;
                            if (range <= Fix64.Zero) { Add(right, parentWeight); break; }
                            var rightWeight = Mathf.Clamp01((value - left.threshold) / range);
                            if (rightWeight < Fix64.One) Add(left, parentWeight * (Fix64.One - rightWeight));
                            if (rightWeight > Fix64.Zero) Add(right, parentWeight * rightWeight);
                            break;
                        }
                    }
                }
            }
            finally { workspace.Path.Remove(tree); }
        }

        void Add(AnimatorBlendMotion motion, Fix64 weight)
        {
            if (weight <= Fix64.Zero) return;
            if (motion.blendTree is { } child) Collect(child, weight, workspace.Path.Count);
            else if (motion.clip is { isDestroyed: false } clip)
                workspace.Result[clip] = workspace.Result.GetValueOrDefault(clip) + weight;
        }
    }

    public void Validate() => EnumerateTrees().ToArray();
    internal bool UsesParameter(string name)
    {
        var path = _parameterPath; var visited = 0;
        return Visit(this, 0);

        bool Visit(AnimatorBlendTree tree, int depth)
        {
            Guard(tree, path, depth, ref visited);
            try
            {
                if (tree.parameter == name || tree.blendType == AnimatorBlendTreeType.Cartesian2D && tree.parameterY == name)
                    return true;
                foreach (var motion in tree.motions)
                    if (motion.blendTree is { } child && Visit(child, depth + 1)) return true;
                return false;
            }
            finally { path.Remove(tree); }
        }
    }

    private readonly HashSet<AnimatorBlendTree> _parameterPath = [];
    internal IEnumerable<AnimationClip> ResolveClips() => EnumerateTrees().SelectMany(tree => tree.motions)
        .Where(motion => motion.blendTree is null).Select(motion => motion.clip).OfType<AnimationClip>()
        .Where(clip => !clip.isDestroyed).Distinct();

    internal IEnumerable<AnimatorBlendTree> EnumerateTrees()
    {
        var result = new List<AnimatorBlendTree>(); var path = new HashSet<AnimatorBlendTree>(); var visited = 0;
        Visit(this, 0); return result;
        void Visit(AnimatorBlendTree tree, int depth)
        {
            Guard(tree, path, depth, ref visited);
            try { result.Add(tree); foreach (var motion in tree.motions) if (motion.blendTree is { } child) Visit(child, depth + 1); }
            finally { path.Remove(tree); }
        }
    }

    internal AnimatorBlendTree Clone()
    {
        Validate();
        var copies = new Dictionary<AnimatorBlendTree, AnimatorBlendTree>();
        return Copy(this);
        AnimatorBlendTree Copy(AnimatorBlendTree tree)
        {
            if (copies.TryGetValue(tree, out var existing)) return existing;
            var copy = new AnimatorBlendTree { blendType = tree.blendType, parameter = tree.parameter, parameterY = tree.parameterY };
            copies.Add(tree, copy);
            copy.motions = tree.motions.Select(motion => new AnimatorBlendMotion
            { clip = motion.clip, threshold = motion.threshold, position = motion.position, blendTree = motion.blendTree is { } child ? Copy(child) : null }).ToList();
            return copy;
        }
    }

    private static void Guard(AnimatorBlendTree tree, HashSet<AnimatorBlendTree> path, int depth, ref int visited)
    {
        if (depth > 16 || ++visited > 512 || tree.motions.Count > 128)
            throw new InvalidDataException("Blend trees support at most 16 nested levels, 512 trees and 128 motions per tree.");
        if (!Enum.IsDefined(tree.blendType)) throw new InvalidDataException("Unknown blend tree type.");
        if (!path.Add(tree)) throw new InvalidDataException("A blend tree cannot contain itself through a nested motion.");
    }

    private AnimatorBlendMotion[] GetOrderedMotions(out int count)
    {
        var changed = _snapshotCount != motions.Count;
        if (_motionSnapshots.Length < motions.Count)
            _motionSnapshots = new MotionSnapshot[Math.Max(8, motions.Count * 2)];
        for (var index = 0; index < motions.Count; index++)
        {
            var motion = motions[index];
            var clip = motion.clip;
            ref var previous = ref _motionSnapshots[index];
            changed |= !ReferenceEquals(previous.Motion, motion) || !ReferenceEquals(previous.Clip, clip) ||
                !ReferenceEquals(previous.Tree, motion.blendTree) || previous.Threshold != motion.threshold ||
                previous.Position != motion.position;
            previous = new(motion, clip, motion.blendTree, motion.threshold, motion.position);
        }
        if (!changed) { count = _orderedCount; return _orderedCache; }
        if (_snapshotCount > motions.Count) Array.Clear(_motionSnapshots, motions.Count, _snapshotCount - motions.Count);
        _snapshotCount = motions.Count;
        if (_orderedCache.Length < motions.Count) _orderedCache = new AnimatorBlendMotion[Math.Max(8, motions.Count * 2)];
        var previousCount = _orderedCount;
        _orderedCount = 0;
        for (var index = 0; index < motions.Count; index++)
        {
            var motion = motions[index];
            if (motion.blendTree is null && motion.clip is not { isDestroyed: false }) continue;
            var insert = _orderedCount;
            while (insert > 0 && _orderedCache[insert - 1].threshold > motion.threshold)
            { _orderedCache[insert] = _orderedCache[insert - 1]; insert--; }
            _orderedCache[insert] = motion; _orderedCount++;
        }
        if (previousCount > _orderedCount) Array.Clear(_orderedCache, _orderedCount, previousCount - _orderedCount);
        count = _orderedCount; return _orderedCache;
    }

    private void CollectCartesian(AnimatorBlendMotion[] source, int sourceCount, Fix64 valueX, Fix64 valueY, Fix64 parentWeight,
        Action<AnimatorBlendTree, Fix64, int> collect, ResolveWorkspace workspace)
    {
        // Normalized gradient bands. Duplicate positions are treated as one sample location
        // and share its contribution, making reordering equivalent motions deterministic.
        // Each tree owns its scratch data: a nested Cartesian tree must not overwrite
        // the parent's groups while the parent is still distributing its weights.
        var groups = _cartesian ??= new CartesianWorkspace();
        var groupCount = 0;
        for (var i = 0; i < sourceCount; i++)
        {
            var motion = source[i]; var group = 0;
            while (group < groupCount && groups.Positions[group] != motion.position) group++;
            if (group == groupCount) { groups.Positions[group] = motion.position; groups.Counts[group] = 0; groupCount++; }
            groups.Counts[group]++;
        }
        var weights = groups.Weights; var total = Fix64.Zero; var value = new Vector2(valueX, valueY);
        var closest = 0; var closestDistance = Fix64.FromRaw(long.MaxValue);
        for (var i = 0; i < groupCount; i++)
        {
            var offset = value - groups.Positions[i];
            var distance = Vector2.Dot(offset, offset);
            if (distance < closestDistance) { closestDistance = distance; closest = i; }
            var weight = Fix64.One;
            for (var j = 0; j < groupCount; j++)
            {
                if (i == j) continue;
                var direction = groups.Positions[j] - groups.Positions[i];
                var length = Vector2.Dot(direction, direction);
                if (length > 0) weight = Fix64.Min(weight, Mathf.Clamp01(1 - Vector2.Dot(offset, direction) / length));
            }
            weights[i] = weight; total += weight;
        }
        if (total <= 0) { weights[closest] = 1; total = 1; }
        for (var i = 0; i < groupCount; i++)
            if (weights[i] > 0)
                for (var j = 0; j < sourceCount; j++)
                    if (source[j].position == groups.Positions[i])
                    {
                        var weight = parentWeight * (weights[i] / total / groups.Counts[i]);
                        if (weight <= Fix64.Zero) continue;
                        if (source[j].blendTree is { } child) collect(child, weight, workspace.Path.Count);
                        else if (source[j].clip is { isDestroyed: false } clip)
                            workspace.Result[clip] = workspace.Result.GetValueOrDefault(clip) + weight;
                    }
    }

    private sealed class ResolveWorkspace
    {
        internal readonly Dictionary<AnimationClip, Fix64> Result = [];
        internal readonly List<(AnimationClip Clip, Fix64 Weight)> Output = [];
        internal readonly HashSet<AnimatorBlendTree> Path = [];
        internal int Visited;
    }

    private readonly record struct MotionSnapshot(AnimatorBlendMotion Motion, AnimationClip? Clip,
        AnimatorBlendTree? Tree, Fix64 Threshold, Vector2 Position);

    private sealed class CartesianWorkspace
    {
        internal readonly Vector2[] Positions = new Vector2[128];
        internal readonly int[] Counts = new int[128];
        internal readonly Fix64[] Weights = new Fix64[128];
    }
}

public sealed class AnimatorBlendMotion
{
    private AnimationClip? _clip;
    private string _clipPath = string.Empty;
    private long _clipGeneration = -1;
    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public string clipPath
    {
        get => !string.IsNullOrWhiteSpace(_clip?.assetPath) ? _clip.assetPath : _clipPath;
        set { _clipPath = value ?? string.Empty; _clip = null; _clipGeneration = -1; }
    }
    [FormerlySerializedAs("clipPath")]
    public AnimationClip? clip
    {
        get
        {
            if (_clip is { isDestroyed: false }) return _clip;
            if (string.IsNullOrWhiteSpace(_clipPath)) return null;
            if (_clipGeneration != BAsset.CacheGeneration || _clip?.isDestroyed == true)
            { _clip = BAsset.Load<AnimationClip>(_clipPath); _clipGeneration = BAsset.CacheGeneration; }
            return _clip is { isDestroyed: false } ? _clip : null;
        }
        set { _clip = value; _clipPath = value?.assetPath ?? string.Empty; _clipGeneration = BAsset.CacheGeneration; }
    }
    public Fix64 threshold { get; set; }
    public Vector2 position { get; set; }
    public AnimatorBlendTree? blendTree { get; set; }
}
