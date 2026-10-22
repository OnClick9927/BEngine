namespace BEngine;

[CreateAssetMenu(fileName = "New Timeline", menuName = "Animation/Timeline", order = 112)]
[EditorIcon("Timeline.png")]
public sealed class TimelineAsset : ScriptableObject
{
    public List<TimelineTrack> tracks { get; set; } = [];
    public Fix64 duration
    {
        get
        {
            var maximum = Fix64.Zero;
            foreach (var track in tracks)
            foreach (var clip in track.clips)
                maximum = Mathf.Max(maximum, clip.end);
            return maximum;
        }
    }

    public void Save(string path) => TimelineAssetSerialization.Save(this, path);
    public static TimelineAsset Load(string path) => TimelineAssetSerialization.Load(path);
}
