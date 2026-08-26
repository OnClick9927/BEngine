using BEngine.Documents;

namespace BEngine.Animation;

[CreateAssetMenu(fileName = "New Animation", menuName = "Animation/Animation Clip", order = 110)]
[EditorIcon("Icons/Assets/AssetAnimation.png")]
public sealed class AnimationClip : ScriptableObject
{
    public Fix64 frameRate { get; set; } = 60;
    public WrapMode wrapMode { get; set; } = WrapMode.Once;
    public bool legacy { get; set; }
    public List<AnimationBinding> bindings { get; set; } = [];
    public List<AnimationEvent> events { get; set; } = [];
    public bool empty => bindings.Count == 0;
    public Fix64 length => bindings.SelectMany(binding => binding.curve.keys).Select(key => key.time)
        .Concat(events.Select(item => item.time)).DefaultIfEmpty(Fix64.Zero).Max();

    public void SetCurve(string relativePath, Type type, string propertyName, AnimationCurve? curve)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        var binding = bindings.FirstOrDefault(item => item.relativePath == relativePath &&
            item.componentType == type.FullName && item.propertyName == propertyName);
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
    }

    public void AddEvent(AnimationEvent animationEvent) { ArgumentNullException.ThrowIfNull(animationEvent); events.Add(animationEvent); events.Sort((a, b) => a.time.CompareTo(b.time)); }
    public void ClearCurves() => bindings.Clear();
    public void SampleAnimation(GameObject gameObject, Fix64 time) => AnimationSampler.Sample(this, gameObject, time);
    public void Save(string path) => Document.SaveBObject<AnimationClipDocument>(this, path);
    public static AnimationClip Load(string path) =>
        Document.LoadBObject<AnimationClipDocument, AnimationClip>(path);
}
