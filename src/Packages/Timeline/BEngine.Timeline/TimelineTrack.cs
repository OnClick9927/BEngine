namespace BEngine;

public sealed class TimelineTrack
{
    public string id { get; set; } = Guid.NewGuid().ToString("N");
    public string name { get; set; } = "Track";
    public TimelineTrackType type { get; set; }
    public bool muted { get; set; }
    public bool locked { get; set; }
    public bool controlActivation { get; set; } = true;
    public List<TimelineClip> clips { get; set; } = [];
}
