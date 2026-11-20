namespace BEngine.Rendering;

// The editor supplies a resolver for its active project. Runtime hosts read packaged bytes directly.
internal readonly record struct ImportedTextureSource(string ArtifactPath, TextureFilterMode Filter, TextureWrapMode Wrap);
internal static class TextureImportResolver
{
    internal static Func<string, ImportedTextureSource?>? Resolve { get; set; }

    internal static void UnregisterAssembly(System.Reflection.Assembly assembly)
    {
        if (Resolve is not { } current) return;
        foreach (var handler in current.GetInvocationList())
            if (handler.Method.Module.Assembly == assembly || handler.Target?.GetType().Assembly == assembly)
                Resolve -= (Func<string, ImportedTextureSource?>)handler;
    }
}
