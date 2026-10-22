namespace BEngine.PostProcessing;

public sealed class Vignette : PostProcessEffectSettings
{
    public ColorParameter color { get; set; } = new(Color.black, true);
    public Fix64Parameter intensity { get; set; } = new(Fix64.Zero, true);
    public Fix64Parameter smoothness { get; set; } = new(Fix64.Parse("0.2"), true);
    public Fix64Parameter roundness { get; set; } = new(Fix64.One, true);

    public override bool IsEnabledAndSupported() => active && intensity.value > Fix64.Zero;
}
