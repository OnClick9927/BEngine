using System.Reflection;

namespace BEngine.Editor;

/// <summary>A property supported by the runtime animation sampler.</summary>
public sealed class AnimationProperty
{
    public string RelativePath { get; }
    public string ComponentType { get; }
    public string PropertyName { get; }
    public string DisplayName => $"{(RelativePath.Length == 0 ? "Root" : RelativePath)}/{Component.GetType().Name}/{PropertyName}";
    public Component Component { get; }
    internal Func<AnimationValue> Read { get; }
    internal Action<AnimationValue> Write { get; }
    public AnimationBindingKind Kind { get; }
    public Type ValueType { get; }
    internal AnimationProperty(string path, AnimationAccessor accessor)
    {
        RelativePath = path; Component = accessor.Component; ComponentType = Component.GetType().FullName!;
        PropertyName = accessor.Property; Read = accessor.Read; Write = accessor.Write;
        Kind = accessor.Kind; ValueType = accessor.ValueType;
    }
    internal AnimationBinding CreateBinding() => new()
    {
        relativePath = RelativePath, componentType = ComponentType, propertyName = PropertyName,
        kind = Kind, objectType = ValueType.FullName!
    };
    internal bool Matches(AnimationBinding binding) => binding.relativePath == RelativePath &&
        (binding.componentType == ComponentType || binding.componentType == Component.GetType().Name) &&
        binding.propertyName.Replace("m_Local", "local", StringComparison.OrdinalIgnoreCase) == PropertyName;
}

public readonly record struct AnimationKeySelection(int Binding, Fix64 Time);

/// <summary>Timeline operations shared by pointer, keyboard and command controls.</summary>
public static class AnimationTimelineEditing
{
    public static Fix64 SnapTime(AnimationClip clip, Fix64 time) =>
        Mathf.Round(Fix64.Clamp(time, 0, 86400) * Rate(clip)) / Rate(clip);
    public static Fix64 Rate(AnimationClip clip) => Fix64.Clamp(clip.frameRate, 1, 240);
    public static IReadOnlyList<AnimationProperty> GetProperties(GameObject root)
    {
        var result = new List<AnimationProperty>();
        if (root.isDestroyed) return result;
        var pending = new Stack<(Transform, string)>(); pending.Push((root.transform, ""));
        while (pending.TryPop(out var entry))
        {
            var (node, path) = entry;
            foreach (var component in node.gameObject.components)
            {
                if (component.isDestroyed) continue;
                var properties = component is Transform
                    ? new[] { "localPosition.x", "localPosition.y", "localRotation", "localScale.x", "localScale.y" }
                    : RuntimeTypeCache.GetSerializableMembers(component.GetType(), inspectorOnly: true)
                        .Where(member => !member.IsDefined(typeof(HideInInspectorAttribute), true))
                        .SelectMany(member => RuntimeTypeCache.GetMemberAccessor(member).ValueType == typeof(Color)
                            ? new[] { member.Name + ".r", member.Name + ".g", member.Name + ".b", member.Name + ".a" }
                            : RuntimeTypeCache.GetMemberAccessor(member).ValueType == typeof(Vector2)
                                ? new[] { member.Name + ".x", member.Name + ".y" } : new[] { member.Name });
                foreach (var property in properties)
                    if (AnimationAccessor.Create(component, property) is { } accessor) result.Add(new(path, accessor));
            }
            for (var i = node.children.Count - 1; i >= 0; i--)
            {
                var child = node.children[i];
                pending.Push((child, path.Length == 0 ? child.gameObject.name : path + "/" + child.gameObject.name));
            }
        }
        return result.DistinctBy(property => (property.RelativePath, property.ComponentType, property.PropertyName)).ToArray();
    }

    public static AnimationBinding AddProperty(AnimationClip clip, AnimationProperty property, Fix64 time)
    {
        var existing = clip.bindings.FirstOrDefault(property.Matches);
        if (existing is not null) return existing;
        var value = property.Read(); var snapped = SnapTime(clip, time);
        var binding = property.CreateBinding();
        binding.SetValue(0, value);
        if (snapped > 0) binding.SetValue(snapped, value);
        AnimationEditing.Edit(clip, "Add Animated Property", () => clip.bindings.Add(binding));
        return binding;
    }

    public static void DeleteKeys(AnimationClip clip, IEnumerable<AnimationKeySelection> selected)
    {
        var selection = selected.ToHashSet();
        if (selection.Count == 0) return;
        AnimationEditing.Edit(clip, "Delete Animation Keys", () =>
        {
            foreach (var group in selection.GroupBy(item => item.Binding))
                if ((uint)group.Key < (uint)clip.bindings.Count)
                { var times = group.Select(item => item.Time).ToHashSet(); var curve = clip.bindings[group.Key].curve;
                    curve.keys = curve.keys.Where(key => !times.Contains(key.time)).ToArray(); }
        });
    }

    internal static void Upsert(AnimationCurve curve, Keyframe key)
    {
        var index = Array.FindIndex(curve.keys, item => item.time == key.time);
        if (index < 0) curve.AddKey(key); else curve.MoveKey(index, key);
    }
}

internal sealed class AnimationPreviewSession : IEditorPreview
{
    private static readonly HashSet<AnimationPreviewSession> Sessions = [];
    private readonly Dictionary<(Component, string), PreviewValue> _values = [];
    private readonly Action? _stopped;
    private readonly Action? _beforeStop;
    private bool _recording, _disposing;
    internal AnimationPreviewSession(Action? stopped = null, Action? beforeStop = null) { _stopped = stopped; _beforeStop = beforeStop; }
    internal AnimationValue Original(AnimationProperty property) => _values.TryGetValue((property.Component, property.PropertyName), out var value) ? value.Original : property.Read();
    internal void SetRecording(AnimationClip clip, GameObject target, bool recording)
    {
        if (recording) EndOtherPreviews(target);
        _recording = recording;
        if (recording) foreach (var property in AnimationTimelineEditing.GetProperties(target)) Track(property);
        Sessions.Add(this); EditorPreviewRegistry.Register(this);
    }
    private void Track(AnimationProperty property)
    {
        var key = (property.Component, property.PropertyName);
        if (!_values.ContainsKey(key)) _values.Add(key, new PreviewValue(property));
    }
    internal IReadOnlyList<(AnimationProperty Property, AnimationValue Value)> Changes() => _values.Values
        .Where(value => !value.Property.Component.isDestroyed && value.Property.Read() != value.SampledValue)
        .Select(value => (value.Property, value.Property.Read())).ToArray();
    internal void Sample(AnimationClip clip, GameObject target, Fix64 time)
    {
        if (_disposing) return;
        EndOtherPreviews(target);
        var properties = AnimationTimelineEditing.GetProperties(target);
        foreach (var property in properties)
            if (_recording || clip.bindings.Any(property.Matches)) Track(property);
        foreach (var value in _values.Values) value.Restore(preserveEdit: !_recording);
        clip.SampleAnimation(target, time);
        foreach (var value in _values.Values) value.Sampled();
        Sessions.Add(this); EditorPreviewRegistry.Register(this); SceneView.RepaintAll();
    }
    private void EndOtherPreviews(GameObject target)
    {
        if (Sessions.Count == 0 || Sessions.Count == 1 && Sessions.Contains(this)) return;
        var components = AnimationTimelineEditing.GetProperties(target).Select(property => property.Component).ToHashSet();
        foreach (var session in Sessions.Where(session => session != this && session._values.Keys.Any(key => components.Contains(key.Item1))).ToArray())
            session.Dispose();
    }

    // Undo must capture authored component values, never a sampled animation pose.
    public T CaptureOriginalState<T>(BObject target, Func<T> capture)
    {
        if (target is not Component) return capture();
        var values = _values.Values
            .Where(value => ReferenceEquals(value.Property.Component, target))
            .Select(value => (Value: value, Current: value.Property.Read())).ToArray();
        try
        {
            foreach (var entry in values)
            {
                if (!_recording && entry.Current != entry.Value.SampledValue) entry.Value.KeepEdit(entry.Current);
                entry.Value.Property.Write(entry.Value.Original);
            }
            return capture();
        }
        finally { foreach (var entry in values) if (!entry.Value.Property.Component.isDestroyed) entry.Value.Property.Write(entry.Current); }
    }

    // Serialized inspectors record directly to the clip. Pure animation edits should
    // not add a scene Undo operation or mark the scene dirty.
    public IReadOnlySet<BObject> RecordInspectorChanges(IReadOnlyCollection<UndoPropertyModification> modifications)
    {
        var recordedTargets = new HashSet<BObject>();
        AnimationPreviewSession[] active = _recording ? [this] : [];
        if (active.Length == 0) return recordedTargets;
        var recordedSessions = new List<AnimationPreviewSession>();
        foreach (var session in active)
        {
            if (session._beforeStop is not null &&
                EditorFeatureGuard.Invoke("Animation Inspector recording", session._beforeStop))
                recordedSessions.Add(session);
            else
                // Failed recording remains an ordinary Inspector edit with normal Undo.
                // Ending preview must preserve that edit rather than discard it as recorded data.
                session._recording = false;
        }
        foreach (var group in modifications.GroupBy(modification => modification.currentValue.Target))
        {
            bool Supported(UndoPropertyModification modification)
            {
                var path = modification.currentValue.PropertyPath.Replace("m_Local", "local", StringComparison.OrdinalIgnoreCase);
                return recordedSessions.Any(session => session._values.Values.Any(value => ReferenceEquals(value.Property.Component, group.Key) &&
                    (value.Property.PropertyName == path || value.Property.PropertyName.StartsWith(path + ".", StringComparison.Ordinal))));
            }
            if (group.All(Supported)) recordedTargets.Add(group.Key);
        }
        return recordedTargets;
    }

    internal static void StopAll()
    {
        foreach (var session in Sessions.ToArray())
            EditorFeatureGuard.Invoke("Animation preview stop", session.Dispose);
    }
    public void Dispose()
    {
        if (_disposing) return;
        _disposing = true;
        try
        {
            if (_beforeStop is not null) EditorFeatureGuard.Invoke("Animation preview finish recording", _beforeStop);
            foreach (var value in _values.Values)
                EditorFeatureGuard.Invoke($"Animation preview restore {value.Property.Component.GetType().FullName}.{value.Property.PropertyName}",
                    () => value.Restore(preserveEdit: !_recording));
        }
        finally
        {
            _values.Clear(); _recording = false; Sessions.Remove(this); EditorPreviewRegistry.Unregister(this); _disposing = false;
            if (_stopped is not null) EditorFeatureGuard.Invoke("Animation preview transport stopped", _stopped);
        }
    }
    private sealed class PreviewValue(AnimationProperty property)
    {
        internal AnimationProperty Property { get; } = property;
        private AnimationValue _original = property.Read();
        internal AnimationValue Original => _original;
        internal void KeepEdit(AnimationValue value) => _original = value;
        internal AnimationValue SampledValue { get; private set; } = property.Read();
        internal void Sampled() { if (!Property.Component.isDestroyed) SampledValue = Property.Read(); }
        internal void Restore(bool preserveEdit)
        {
            if (Property.Component.isDestroyed) return;
            var current = Property.Read();
            if (preserveEdit && current != SampledValue) _original = current;
            Property.Write(_original); SampledValue = _original;
        }
    }
}

