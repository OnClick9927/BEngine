namespace BEngine.PostProcessing;

/// <summary>Registration point for package and project supplied effects.</summary>
public static class PostProcessEffectRegistry
{
    private static readonly Dictionary<string, Func<PostProcessEffectSettings>> Factories =
        new(StringComparer.OrdinalIgnoreCase);

    static PostProcessEffectRegistry()
    {
        Register<ChromaticAberration>("chromaticAberration");
        Register<FilmGrain>("filmGrain");
        Register<LensDistortion>("lensDistortion");
        Register<ColorLookup>("colorLookup");
    }

    public static void Register<T>(string id, Func<T>? factory = null)
        where T : PostProcessEffectSettings, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Factories[id] = factory is null ? static () => new T() : () => factory();
    }

    public static bool Unregister(string id) => Factories.Remove(id);
    public static bool TryCreate(string id, out PostProcessEffectSettings effect)
    {
        if (Factories.TryGetValue(id, out var factory)) { effect = factory(); return true; }
        effect = null!; return false;
    }
    public static IReadOnlyCollection<string> ids => Factories.Keys.ToArray();
}
