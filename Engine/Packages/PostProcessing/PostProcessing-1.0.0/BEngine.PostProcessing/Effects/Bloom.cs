namespace BEngine.PostProcessing;

public sealed class Bloom : PostProcessEffectSettings
{
    public Fix64Parameter intensity { get; set; } = new(Fix64.Zero, true);
    public Fix64Parameter threshold { get; set; } = new(Fix64.One, true);
    public Fix64Parameter scatter { get; set; } = new(Fix64.Parse("0.7"), true);

    public override bool IsEnabledAndSupported() => active && intensity.value > Fix64.Zero;
}
