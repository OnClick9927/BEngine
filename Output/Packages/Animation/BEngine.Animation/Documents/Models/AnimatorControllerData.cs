using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.Animation;
internal sealed class AnimatorControllerData
{
    public string Format { get; set; } = "BEngine.AnimatorController"; public int Version { get; set; } = 1;
    public string Name { get; set; } = "New Animator Controller"; public string DefaultState { get; set; } = string.Empty;
    public List<AnimatorParameterData> Parameters { get; set; } = []; public List<AnimatorStateData> States { get; set; } = [];
}
