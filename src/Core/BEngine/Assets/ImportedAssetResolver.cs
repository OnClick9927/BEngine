namespace BEngine;

// Editor hosts map source files such as PSD documents to their imported runtime artifact.
// Players receive artifact bytes from bundles and do not install this resolver.
internal static class ImportedAssetResolver
{
    internal static Func<string, Type, string?>? ResolveArtifactPath { get; set; }
}
