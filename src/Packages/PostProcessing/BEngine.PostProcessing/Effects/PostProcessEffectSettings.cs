namespace BEngine.PostProcessing;

public abstract class PostProcessEffectSettings
{
    public bool active { get; set; } = true;
    public abstract bool IsEnabledAndSupported();
}
