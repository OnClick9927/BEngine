using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.Animation;
public sealed class AnimatorConditionDocument : Document { public string Parameter { get; set; } = string.Empty; public AnimatorConditionMode Mode { get; set; } public long Threshold { get; set; } }
