namespace BEngine.ProjectSystem.Editor;

public sealed class ShaderCompilationResult
{
    public IReadOnlyDictionary<string, string> CompiledArtifacts { get; }
    public IReadOnlyList<string> DeletedAssets { get; }
    public IReadOnlyDictionary<string, string> Errors { get; }
    public bool Succeeded => Errors.Count == 0;

    internal ShaderCompilationResult(
        IReadOnlyDictionary<string, string> compiledArtifacts,
        IReadOnlyDictionary<string, string> deletedPointers,
        IReadOnlyDictionary<string, string> errors)
    {
        CompiledArtifacts = compiledArtifacts;
        DeletedPointers = deletedPointers;
        DeletedAssets = deletedPointers.Keys.ToArray();
        Errors = errors;
    }

    internal IReadOnlyDictionary<string, string> DeletedPointers { get; }
}
