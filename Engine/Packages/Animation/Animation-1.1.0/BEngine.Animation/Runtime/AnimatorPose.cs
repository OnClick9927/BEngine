namespace BEngine;

/// <summary>Per-Animator default pose and override-layer mixing for authored bindings.</summary>
internal sealed class AnimatorPose
{
    private sealed record Channel(Component Component, string Property, Func<AnimationValue> Read, Action<AnimationValue> Write, AnimationValue Default, bool RootMotion, AnimationBindingKind Kind, string Path)
    { internal int Index { get; set; } }
    private readonly Dictionary<(Component, string), Channel> _channels = [];
    private readonly Dictionary<AnimationBinding, Channel> _bindingChannels = [];
    private readonly List<Channel> _orderedChannels = [];
    private readonly HashSet<(Component, string)> _active = [];
    private readonly List<(Component, string)> _removed = [];
    private readonly List<AnimationClip> _clips = [];
    private readonly HashSet<AnimationClip> _clipSet = [];
    private Contribution[] _contributions = [];
    private bool[] _sampled = [];
    private int[] _numericOrder = [], _discreteOrder = [];
    private AnimationValue[] _previousValues = [], _referenceValues = [];
    private bool[] _previousIncluded = [], _referenceIncluded = [];
    private int _generation;
    private struct Contribution
    {
        internal Fix64 Sum, Weight, Best, Total;
        internal AnimationValue Value;
        internal bool Numeric, Discrete;
    }
    internal readonly record struct PoseSnapshot(AnimationValue[] Values, bool[] Included, int Count, int Generation);
    internal void Clear()
    {
        _generation++;
        _channels.Clear(); _bindingChannels.Clear(); _orderedChannels.Clear(); _active.Clear(); _removed.Clear();
        _clips.Clear(); _clipSet.Clear();
        _contributions = []; _sampled = []; _numericOrder = []; _discreteOrder = [];
        _previousValues = []; _referenceValues = []; _previousIncluded = []; _referenceIncluded = [];
    }
    internal void BeginLayer() => Array.Clear(_sampled, 0, _orderedChannels.Count);
    internal void ReleaseSnapshots()
    { Array.Clear(_previousValues); Array.Clear(_referenceValues); }
    internal void Register(GameObject root, AnimatorController controller)
    {
        _clips.Clear(); _clipSet.Clear();
        try
        {
            for (var layer = 0; layer < controller.layerCount; layer++)
            {
                if (controller.GetStateMachines(layer).Count == 0)
                    foreach (var state in controller.GetStates(layer)) AddState(state);
                else foreach (var state in controller.GetAllStates(layer)) AddState(state);
            }
            foreach (var layer in controller.layers) AddClip(layer.referencePose);
            Register(root, _clips);
        }
        finally { _clips.Clear(); _clipSet.Clear(); }
        void AddClip(AnimationClip? clip) { if (clip is not null && _clipSet.Add(clip)) _clips.Add(clip); }
        void AddState(AnimatorState state)
        {
            if (state.blendTree is { } tree) foreach (var clip in tree.ResolveClips()) AddClip(clip);
            else AddClip(state.ResolveClip());
        }
    }

    internal void Register(GameObject root, IReadOnlyList<AnimationClip> clips)
    {
        _generation++;
        _bindingChannels.Clear();
        var active = _active; active.Clear();
        for (var clipIndex = 0; clipIndex < clips.Count; clipIndex++)
        {
            var clip = clips[clipIndex];
            if (clip.isDestroyed) continue;
            foreach (var binding in clip.bindings)
            {
                var accessor = AnimationAccessor.Resolve(root, binding);
                if (accessor is null) continue;
                var component = accessor.Component;
                var property = accessor.Property;
                if (_channels.TryGetValue((component, property), out var registered))
                { _bindingChannels[binding] = registered; active.Add((component, property)); continue; }
                var rootMotion = ReferenceEquals(component, root.transform) && (property.StartsWith("localPosition", StringComparison.OrdinalIgnoreCase) || property.Equals("localRotation", StringComparison.OrdinalIgnoreCase));
                _channels[(component, property)] = _bindingChannels[binding] =
                    new Channel(component, property, accessor.Read, accessor.Write, accessor.Read(), rootMotion, accessor.Kind, binding.relativePath.Trim('/'));
                active.Add((component, property));
            }
        }
        _removed.Clear();
        foreach (var key in _channels.Keys)
            if (!active.Contains(key) || key.Item1.isDestroyed) _removed.Add(key);
        foreach (var key in _removed) _channels.Remove(key);
        _removed.Clear(); active.Clear();
        _orderedChannels.Clear();
        foreach (var channel in _channels.Values)
        { channel.Index = _orderedChannels.Count; _orderedChannels.Add(channel); }
        if (_contributions.Length < _orderedChannels.Count)
        {
            var capacity = Math.Max(_orderedChannels.Count, Math.Max(16, _contributions.Length * 2));
            _contributions = new Contribution[capacity]; _sampled = new bool[capacity];
            _numericOrder = new int[capacity]; _discreteOrder = new int[capacity];
            _previousValues = new AnimationValue[capacity]; _referenceValues = new AnimationValue[capacity];
            _previousIncluded = new bool[capacity]; _referenceIncluded = new bool[capacity];
        }
    }

    // Timeline supplies a lazy enumerable. Materialize it once at registration,
    // keeping the hot sampling path on the reusable array representation above.
    internal void Register(GameObject root, IEnumerable<AnimationClip> clips) =>
        Register(root, clips as IReadOnlyList<AnimationClip> ?? clips.ToArray());
    internal void RestoreDefaults(bool skipRootMotion)
    { foreach (var channel in _orderedChannels) if (!channel.Component.isDestroyed && !(skipRootMotion && channel.RootMotion)) channel.Write(channel.Default); }
    internal PoseSnapshot Capture(bool skipRootMotion, bool reference = false)
    {
        var values = reference ? _referenceValues : _previousValues;
        var included = reference ? _referenceIncluded : _previousIncluded;
        for (var i = 0; i < _orderedChannels.Count; i++)
        {
            var channel = _orderedChannels[i];
            included[i] = !channel.Component.isDestroyed && !(skipRootMotion && channel.RootMotion);
            values[i] = included[i] ? channel.Read() : default;
        }
        return new(values, included, _orderedChannels.Count, _generation);
    }
    internal void ApplyLayer(PoseSnapshot previous, Fix64 weight,
        AnimatorControllerLayer layer, PoseSnapshot? reference)
    {
        for (var index = 0; index < previous.Count; index++)
        {
            if (previous.Generation != _generation) return;
            var channel = _orderedChannels[index]; var value = previous.Values[index];
            if (!previous.Included[index] || channel.Component.isDestroyed) continue;
            if (!layer.Includes(channel.Path)) { channel.Write(value); continue; }
            var sampled = channel.Read();
            if (reference is not null && channel.Kind != AnimationBindingKind.Numeric && !_sampled[index])
            { channel.Write(value); continue; }
            if (reference is not null && channel.Kind == AnimationBindingKind.Numeric)
                channel.Write(new(value.Number + (sampled.Number - reference.Value.Values[index].Number) * weight));
            else channel.Write(AnimationValue.Mix(value, sampled, weight, channel.Kind));
        }
    }

    internal void SampleWeightedClips(GameObject root,
        IEnumerable<(AnimationClip Clip, Fix64 Time, Fix64 Weight)> samples,
        bool skipRootMotion = false) => SampleWeightedClips(root,
            samples as IReadOnlyList<(AnimationClip Clip, Fix64 Time, Fix64 Weight)> ?? samples.ToArray(), skipRootMotion);

    internal void SampleWeightedClips(GameObject root,
        IReadOnlyList<(AnimationClip Clip, Fix64 Time, Fix64 Weight)> samples,
        bool skipRootMotion = false)
    {
        var numericCount = 0; var discreteCount = 0;
        var generation = _generation;
        var contributions = _contributions; var numericOrder = _numericOrder; var discreteOrder = _discreteOrder;
        try
        {
            for (var sampleIndex = 0; sampleIndex < samples.Count; sampleIndex++)
            {
                var (clip, time, weight) = samples[sampleIndex];
                if (clip.isDestroyed || weight <= Fix64.Zero) continue;
                foreach (var binding in clip.bindings)
                {
                    if (!_bindingChannels.TryGetValue(binding, out var channel) || channel.Component.isDestroyed ||
                        skipRootMotion && channel.RootMotion) continue;
                    var index = channel.Index; _sampled[index] = true;
                    ref var contribution = ref contributions[index];
                    if (channel.Kind != AnimationBindingKind.Numeric)
                    {
                        var sample = binding.Evaluate(time, channel.Kind);
                        if (!contribution.Discrete) { contribution.Discrete = true; discreteOrder[discreteCount++] = index; }
                        if (weight >= contribution.Best) contribution.Value = sample;
                        contribution.Best = Fix64.Max(weight, contribution.Best); contribution.Total += weight;
                        continue;
                    }
                    if (!contribution.Numeric) { contribution.Numeric = true; numericOrder[numericCount++] = index; }
                    contribution.Sum += binding.curve.Evaluate(time) * weight; contribution.Weight += weight;
                }
            }
            for (var i = 0; i < discreteCount; i++)
            {
                if (generation != _generation) return;
                var index = discreteOrder[i]; var contribution = contributions[index];
                if (contribution.Best >= Fix64.Max(0, 1 - contribution.Total)) _orderedChannels[index].Write(contribution.Value);
            }
            for (var i = 0; i < numericCount; i++)
            {
                if (generation != _generation) return;
                var index = numericOrder[i]; var contribution = contributions[index]; var channel = _orderedChannels[index];
                var remaining = Mathf.Max(Fix64.Zero, Fix64.One - contribution.Weight);
                var denominator = Mathf.Max(Fix64.One, contribution.Weight);
                channel.Write(new((channel.Read().Number * remaining + contribution.Sum) / denominator));
            }
        }
        finally
        {
            for (var i = 0; i < numericCount; i++) contributions[numericOrder[i]] = default;
            for (var i = 0; i < discreteCount; i++) contributions[discreteOrder[i]] = default;
        }
    }
}
