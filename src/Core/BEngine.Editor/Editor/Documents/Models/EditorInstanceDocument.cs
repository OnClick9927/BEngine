using BEngine.Documents;

namespace BEngine.Editor.Documents;

internal sealed class EditorInstanceDocument : Document
{
    public string Format { get; set; } = "BEngine.EditorInstance";
    public int Version { get; set; } = 1;
    public string InstanceId { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public DateTime ProcessStartedUtc { get; set; }
    public string ProjectPath { get; set; } = string.Empty;
}
