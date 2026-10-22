namespace BEngine.VisualScripting;

public enum VisualScriptNodeKind
{
    StartEvent,
    UpdateEvent,
    CustomEvent,
    Branch,
    SetVariable,
    AddNumber,
    CompareNumber,
    SetPosition,
    Log,
    Stop,
    StateEnterEvent, StateUpdateEvent, StateExitEvent, TransitionState,
    CallSubGraph, Wait, CustomAction, MultiplyNumber, DeltaTime, KeyPressed, SetActive
}

public enum VisualScriptValueKind { Boolean, Number, String, Vector2 }

public enum VisualScriptComparison
{
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual
}

public sealed class VisualScriptValue
{
    public VisualScriptValueKind kind { get; set; }
    public bool boolean { get; set; }
    public Fix64 number { get; set; }
    public string text { get; set; } = string.Empty;
    public Vector2 vector { get; set; }

    public static VisualScriptValue Boolean(bool value) => new() { kind = VisualScriptValueKind.Boolean, boolean = value };
    public static VisualScriptValue Number(Fix64 value) => new() { kind = VisualScriptValueKind.Number, number = value };
    public static VisualScriptValue String(string? value) => new() { kind = VisualScriptValueKind.String, text = value ?? string.Empty };
    public static VisualScriptValue Vector(Vector2 value) => new() { kind = VisualScriptValueKind.Vector2, vector = value };

    public VisualScriptValue Clone() => new()
    {
        kind = kind,
        boolean = boolean,
        number = number,
        text = text,
        vector = vector
    };

    public override string ToString() => kind switch
    {
        VisualScriptValueKind.Boolean => boolean ? "true" : "false",
        VisualScriptValueKind.Number => number.ToString(),
        VisualScriptValueKind.String => text,
        VisualScriptValueKind.Vector2 => vector.ToString(),
        _ => string.Empty
    };
}

public sealed class VisualScriptVariable
{
    public string name { get; set; } = "variable";
    public VisualScriptValue value { get; set; } = VisualScriptValue.Boolean(false);
}

public sealed class VisualScriptNode
{
    public string id { get; set; } = Guid.NewGuid().ToString("N");
    public VisualScriptNodeKind kind { get; set; }
    public Vector2 position { get; set; }
    public string variable { get; set; } = string.Empty;
    public string secondaryVariable { get; set; } = string.Empty;
    public string text { get; set; } = string.Empty;
    public VisualScriptValue value { get; set; } = VisualScriptValue.Boolean(false);
    public VisualScriptComparison comparison { get; set; }
    public bool relative { get; set; }
    public bool breakpoint { get; set; }
    public string graphPath { get; set; } = string.Empty;
    public string actionId { get; set; } = string.Empty;
}

public sealed class VisualScriptConnection
{
    public string id { get; set; } = Guid.NewGuid().ToString("N");
    public string source { get; set; } = string.Empty;
    public string sourcePort { get; set; } = "Out";
    public string destination { get; set; } = string.Empty;
    public string destinationPort { get; set; } = "In";
}
