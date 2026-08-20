using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.Animation;
public sealed class AnimatorControllerDocument : Document
{
    public string Format { get; set; } = "BEngine.AnimatorController"; public int Version { get; set; } = 1;
    public string Name { get; set; } = "New Animator Controller"; public string DefaultState { get; set; } = string.Empty;
    public List<AnimatorParameterDocument> Parameters { get; set; } = []; public List<AnimatorStateDocument> States { get; set; } = [];
}
