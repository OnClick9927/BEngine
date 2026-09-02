using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.Animation;
internal sealed class AnimationEventData  { public long Time { get; set; } public string FunctionName { get; set; } = string.Empty; public string StringParameter { get; set; } = string.Empty; public int IntParameter { get; set; } public long FloatParameter { get; set; } }
