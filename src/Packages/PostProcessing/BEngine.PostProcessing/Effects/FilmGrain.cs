namespace BEngine.PostProcessing;

/// <summary>Animated film grain applied after color grading.</summary>
public sealed class FilmGrain : PostProcessEffectSettings
{
    public Fix64Parameter intensity { get; set; } = new(Fix64.Zero, true);
    public Fix64Parameter response { get; set; } = new(Fix64.Parse("0.8"), true);
    public override bool IsEnabledAndSupported() => active && intensity.value > Fix64.Zero;
}
