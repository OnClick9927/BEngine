namespace BEngine.PostProcessing;

/// <summary>Separates the color channels near the edge of the frame.</summary>
public sealed class ChromaticAberration : PostProcessEffectSettings
{
    public Fix64Parameter intensity { get; set; } = new(Fix64.Zero, true);
    public bool fastMode { get; set; } = true;
    public override bool IsEnabledAndSupported() => active && intensity.value > Fix64.Zero;
}
