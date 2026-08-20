using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.Animation;
public sealed class AnimationBindingDocument : Document
{
    public string RelativePath { get; set; } = string.Empty; public string ComponentType { get; set; } = typeof(Transform).FullName!;
    public string PropertyName { get; set; } = string.Empty; public WrapMode PreWrapMode { get; set; } = WrapMode.ClampForever;
    public WrapMode PostWrapMode { get; set; } = WrapMode.ClampForever; public List<KeyframeDocument> Keys { get; set; } = [];
}
