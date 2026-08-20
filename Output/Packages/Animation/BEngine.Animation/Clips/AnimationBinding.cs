using BEngine.Serialization;

namespace BEngine.Animation;

public sealed class AnimationBinding
{
    public string relativePath { get; set; } = string.Empty;
    public string componentType { get; set; } = typeof(Transform).FullName!;
    public string propertyName { get; set; } = string.Empty;
    public AnimationCurve curve { get; set; } = new();
}
