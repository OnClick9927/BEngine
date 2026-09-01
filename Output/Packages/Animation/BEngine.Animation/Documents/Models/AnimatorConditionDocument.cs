using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.Animation;
internal sealed class AnimatorConditionData  { public string Parameter { get; set; } = string.Empty; public AnimatorConditionMode Mode { get; set; } public long Threshold { get; set; } }
