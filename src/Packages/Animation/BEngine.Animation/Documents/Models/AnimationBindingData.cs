using BEngine.Serialization;

namespace BEngine;
internal sealed class AnimationBindingData
{
    public string RelativePath { get; set; } = string.Empty; public string ComponentType { get; set; } = typeof(Transform).FullName!;
    public string PropertyName { get; set; } = string.Empty; public WrapMode PreWrapMode { get; set; } = WrapMode.ClampForever;
    public WrapMode PostWrapMode { get; set; } = WrapMode.ClampForever; public List<KeyframeData> Keys { get; set; } = [];
}
