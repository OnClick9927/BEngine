namespace BEngine.ProjectSystem.Editor;

public sealed class ShaderCompilationResult
{
    internal object? Registration { get; set; }
    public IReadOnlyDictionary<string, string> CompiledArtifacts { get; }
    /// <summary>Source asset to retired artifact path, interpreted by the registered compiler.</summary>
    public IReadOnlyDictionary<string, string> DeletedArtifacts { get; }
    public IReadOnlyList<string> DeletedAssets { get; }
    public IReadOnlyDictionary<string, string> Errors { get; }
    public bool Succeeded => Errors.Count == 0;

    public ShaderCompilationResult(
        IReadOnlyDictionary<string, string> compiledArtifacts,
        IReadOnlyDictionary<string, string> deletedPointers,
        IReadOnlyDictionary<string, string> errors)
    {
        CompiledArtifacts = compiledArtifacts;
        DeletedArtifacts = deletedPointers;
        DeletedAssets = deletedPointers.Keys.ToArray();
        Errors = errors;
    }

}
