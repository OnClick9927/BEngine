namespace BEngine.Editor;

internal static class BPackageArchiveFormat
{
    internal const string Format = "BEngine.BPackage";
    internal const int Version = 1;
    internal const string ManifestPath = "manifest.yaml";
    internal const string PayloadPrefix = "Assets/";
    internal const int MaximumManifestBytes = 4 * 1024 * 1024;
    internal static DateTimeOffset DeterministicTimestamp { get; } =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
