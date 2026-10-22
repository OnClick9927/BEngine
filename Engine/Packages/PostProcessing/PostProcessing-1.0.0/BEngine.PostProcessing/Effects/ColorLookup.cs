namespace BEngine.PostProcessing;

/// <summary>A lightweight color lookup hook.  A custom renderer can provide the
/// texture; the built-in renderer uses contribution as a safe no-op when no
/// lookup texture is assigned.</summary>
public sealed class ColorLookup : PostProcessEffectSettings
{
    public Texture? texture { get; set; }
    public Fix64Parameter contribution { get; set; } = new(Fix64.One, true);
    public override bool IsEnabledAndSupported() => active && texture is { isDestroyed: false } && contribution.value > Fix64.Zero;
}
