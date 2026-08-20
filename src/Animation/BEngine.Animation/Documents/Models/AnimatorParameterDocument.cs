using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.Animation;
public sealed class AnimatorParameterDocument : Document { public string Name { get; set; } = string.Empty; public AnimatorControllerParameterType Type { get; set; } public long DefaultFloat { get; set; } public int DefaultInt { get; set; } public bool DefaultBool { get; set; } }
