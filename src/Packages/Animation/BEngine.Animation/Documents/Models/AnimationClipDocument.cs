using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.Animation;

public sealed class AnimationClipDocument : Document
{
    public string Format { get; set; } = "BEngine.AnimationClip"; public int Version { get; set; } = 1;
    public string Name { get; set; } = "New Animation"; public long FrameRate { get; set; } = 60L << Fix64.FractionalBits;
    public WrapMode WrapMode { get; set; } public bool Legacy { get; set; }
    public List<AnimationBindingDocument> Bindings { get; set; } = []; public List<AnimationEventDocument> Events { get; set; } = [];
}
