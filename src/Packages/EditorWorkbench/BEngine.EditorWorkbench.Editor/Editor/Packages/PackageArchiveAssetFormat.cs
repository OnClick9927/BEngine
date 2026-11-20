using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class PackageArchiveAssetFormat
{
    [ModuleInitializer]
    internal static void Register() => AssetTypeRegistry.Register(".bpackage", "BEnginePackage");
}
