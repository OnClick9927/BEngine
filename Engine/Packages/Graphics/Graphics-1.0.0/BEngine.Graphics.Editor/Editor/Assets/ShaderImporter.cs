namespace BEngine.Editor;

public sealed class ShaderImporter : AssetImporter
{
    public bool strictCompilation { get; set; } = true;
    public int optimizationLevel { get; set; } = 2;

    protected override void ReadSettings(IReadOnlyDictionary<string, string> settings)
    {
        base.ReadSettings(settings);
        strictCompilation = Get(settings, nameof(strictCompilation), true);
        optimizationLevel = Math.Clamp(Get(settings, nameof(optimizationLevel), 2), 0, 3);
    }

    protected override void WriteSettings(IDictionary<string, string> settings)
    {
        base.WriteSettings(settings);
        Set(settings, nameof(strictCompilation), strictCompilation);
        Set(settings, nameof(optimizationLevel), Math.Clamp(optimizationLevel, 0, 3));
    }
}
