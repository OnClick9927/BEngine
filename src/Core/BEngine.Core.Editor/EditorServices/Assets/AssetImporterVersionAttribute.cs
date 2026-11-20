namespace BEngine.Editor;

/// <summary>Increase when importer behavior changes to invalidate existing artifacts on refresh.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class AssetImporterVersionAttribute(int version) : Attribute
{
    public int Version { get; } = version > 0 ? version : throw new ArgumentOutOfRangeException(nameof(version));
}
