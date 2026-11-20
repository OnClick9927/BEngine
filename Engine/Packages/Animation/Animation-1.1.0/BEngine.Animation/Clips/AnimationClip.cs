using BEngine.Serialization;

namespace BEngine;

[CreateAssetMenu(fileName = "New Animation", menuName = "Animation/Animation Clip", order = 110)]
[EditorIcon("Icons/AssetAnimation.png")]
public sealed class AnimationClip : ScriptableObject
{
    public Fix64 frameRate { get; set; } = 60;
    public WrapMode wrapMode { get; set; } = WrapMode.Once;
    public bool legacy { get; set; }
    public List<AnimationBinding> bindings { get; set; } = [];
    public List<AnimationEvent> events { get; set; } = [];
    public bool empty => bindings.Count == 0;
    public Fix64 length
    {
        get
        {
            var maximum = Fix64.Zero;
            foreach (var binding in bindings)
                maximum = Mathf.Max(maximum, binding.curve.EndTime);
            foreach (var animationEvent in events) maximum = Mathf.Max(maximum, animationEvent.time);
            return maximum;
        }
    }

    public void SetCurve(string relativePath, Type type, string propertyName, AnimationCurve? curve)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        var binding = Matches1181();
        [global::BEngine.SkipLoopGuard]
        global::BEngine.AnimationBinding? Matches1181()
        {
            var matchSource1181 = bindings;
            ArgumentNullException.ThrowIfNull(matchSource1181, "source");
            if (matchSource1181.GetType() != typeof(global::System.Collections.Generic.List<global::BEngine.AnimationBinding>))
            {
                foreach (var item in (global::System.Collections.Generic.IEnumerable<global::BEngine.AnimationBinding>)matchSource1181)
                {
                    if (item.relativePath == relativePath && item.componentType == type.FullName && item.propertyName == propertyName)
                        return item;
                }
        
                return default!;
            }
        
            global::System.ReadOnlySpan<global::BEngine.AnimationBinding> matchValues1181 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(matchSource1181);
            foreach (var item in matchValues1181)
            {
                if (item.relativePath == relativePath && item.componentType == type.FullName && item.propertyName == propertyName)
                    return item;
            }
        
            return default!;
        }
        if (curve is null)
        {
            if (binding is not null) bindings.Remove(binding);
            return;
        }
        if (binding is null)
        {
            binding = new AnimationBinding { relativePath = relativePath, componentType = type.FullName!, propertyName = propertyName };
            bindings.Add(binding);
        }
        binding.curve = curve;
        binding.kind = RuntimeTypeCache.TryFindInstanceMember(type, propertyName, out var member) &&
            RuntimeTypeCache.GetMemberAccessor(member).ValueType == typeof(bool) ? AnimationBindingKind.Boolean : AnimationBindingKind.Numeric;
        binding.objectReferences.Clear();
    }

    public void SetObjectReferenceCurve(string relativePath, Type type, string propertyName, ObjectReferenceKeyframe[]? keys)
    {
        if (keys is null) { SetCurve(relativePath, type, propertyName, null); return; }
        ArgumentNullException.ThrowIfNull(type);
        var binding = new AnimationBinding { relativePath = relativePath, componentType = type.FullName!, propertyName = propertyName };
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        binding.SetObjectReferenceKeys(keys);
        if (RuntimeTypeCache.TryFindInstanceMember(type, propertyName, out var member))
        {
            var valueType = RuntimeTypeCache.GetMemberAccessor(member).ValueType;
            if (!typeof(BObject).IsAssignableFrom(valueType) || Query2799())
                throw new ArgumentException("Object key type does not match the animated property.", nameof(keys));
            bool Query2799()
            {
                var querySource2799 = keys;
                ArgumentNullException.ThrowIfNull(querySource2799, "source");
                if (querySource2799.GetType() == typeof(global::BEngine.ObjectReferenceKeyframe[]) || querySource2799.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.ObjectReferenceKeyframe>))
                {
                    global::System.ReadOnlySpan<global::BEngine.ObjectReferenceKeyframe> queryValues2799 = querySource2799.GetType() == typeof(global::BEngine.ObjectReferenceKeyframe[]) ? (global::BEngine.ObjectReferenceKeyframe[])(object)querySource2799 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.ObjectReferenceKeyframe>)(object)querySource2799);
                    foreach (var key in queryValues2799)
                    {
                        if (key.value is not null && !valueType.IsInstanceOfType(key.value))
                            return true;
                    }
                }
                else
                {
                    foreach (var key in querySource2799)
                    {
                        if (key.value is not null && !valueType.IsInstanceOfType(key.value))
                            return true;
                    }
                }
            
                return false;
            }
            binding.objectType = valueType.FullName!;
        }
        bindings.RemoveAll(item => item.relativePath == relativePath && item.componentType == type.FullName && item.propertyName == propertyName);
        bindings.Add(binding);
    }

    public void AddEvent(AnimationEvent animationEvent) { ArgumentNullException.ThrowIfNull(animationEvent); events.Add(animationEvent); events.Sort((a, b) => a.time.CompareTo(b.time)); }
    public void ClearCurves() => bindings.Clear();
    public void SampleAnimation(GameObject gameObject, Fix64 time) { ThrowIfDestroyed(); AnimationSampler.Sample(this, gameObject, time); }
    public void Save(string path) => AnimationClipSerialization.Save(this, path);
    public static AnimationClip Load(string path) => AnimationClipSerialization.Load(path);
}
