namespace BEngine.Editor;

public sealed class TrackEditorKey(object id, Fix64 time, Fix64 value, bool selected = false,
    Fix64 inTangent = default, Fix64 outTangent = default)
{
    public object id { get; } = id;
    public Fix64 time { get; } = time;
    public Fix64 value { get; } = value;
    public bool selected { get; } = selected;
    public Fix64 inTangent { get; } = inTangent;
    public Fix64 outTangent { get; } = outTangent;
}

public sealed class TrackEditorClip(object id, string name, Fix64 start, Fix64 duration,
    Color32 color, bool selected = false)
{
    public object id { get; } = id;
    public string name { get; } = name;
    public Fix64 start { get; } = start;
    public Fix64 duration { get; } = duration;
    public Color32 color { get; } = color;
    public bool selected { get; } = selected;
    public Fix64 blendIn { get; set; }
    public Fix64 blendOut { get; set; }
    public bool blendEditable { get; set; }
}

public sealed class TrackEditorTrack(object id, string name)
{
    public object id { get; } = id;
    public string name { get; } = name;
    public Color32 color { get; set; } = new(116, 179, 214);
    public bool selected { get; set; }
    public bool muted { get; set; }
    public bool locked { get; set; }
    public bool curveVisible { get; set; } = true;
    public List<TrackEditorKey> keys { get; } = [];
    public List<TrackEditorClip> clips { get; } = [];
    public Func<Fix64, Fix64>? sample { get; set; }
}

public enum TrackEditorHeaderAction { Mute, Lock }

public sealed class TrackEditorMarker(object id, Fix64 time, bool selected = false)
{
    public object id { get; } = id;
    public Fix64 time { get; } = time;
    public bool selected { get; } = selected;
}

