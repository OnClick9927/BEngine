namespace BEngine.PostProcessing;

[CreateAssetMenu(fileName = "Post Process Profile", menuName = "Post Processing/Profile", order = 310)]
public sealed class PostProcessProfile : ScriptableObject
{
    public ColorAdjustments colorAdjustments { get; set; } = new();
    public Bloom bloom { get; set; } = new();
    public Vignette vignette { get; set; } = new();
    public ChromaticAberration chromaticAberration { get; set; } = new();
    public FilmGrain filmGrain { get; set; } = new();
    public LensDistortion lensDistortion { get; set; } = new();
    public ColorLookup colorLookup { get; set; } = new();
    public List<PostProcessEffectSettings> customEffects { get; set; } = [];

    public bool HasActiveEffects() =>
        colorAdjustments.IsEnabledAndSupported() || bloom.IsEnabledAndSupported() ||
        vignette.IsEnabledAndSupported() || chromaticAberration.IsEnabledAndSupported() ||
        filmGrain.IsEnabledAndSupported() || lensDistortion.IsEnabledAndSupported() ||
        colorLookup.IsEnabledAndSupported() || customEffects.Any(effect => effect?.IsEnabledAndSupported() == true);

    public T GetOrAddEffect<T>() where T : PostProcessEffectSettings, new()
    {
        var existing = customEffects.OfType<T>().FirstOrDefault();
        if (existing is not null) return existing;
        var created = new T(); customEffects.Add(created); return created;
    }
}
