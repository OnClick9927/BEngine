namespace BEngine.Rendering;

// The editor supplies a resolver for its active project. Runtime hosts read packaged bytes directly.
internal readonly record struct ImportedTextureSource(string ArtifactPath, TextureFilterMode Filter, TextureWrapMode Wrap);
internal static class TextureImportResolver
{
    internal static Func<string, ImportedTextureSource?>? Resolve { get; set; }
}
