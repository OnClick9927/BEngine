using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.Animation;
internal sealed class KeyframeData  { public long Time { get; set; } public long Value { get; set; } public long InTangent { get; set; } public long OutTangent { get; set; } }
