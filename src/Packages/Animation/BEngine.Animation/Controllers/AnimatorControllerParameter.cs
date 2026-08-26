using BEngine.Serialization;

namespace BEngine.Animation;

public sealed class AnimatorControllerParameter
{
    public string name { get; set; } = string.Empty;
    public AnimatorControllerParameterType type { get; set; }
    public Fix64 defaultFloat { get; set; }
    public int defaultInt { get; set; }
    public bool defaultBool { get; set; }
}
