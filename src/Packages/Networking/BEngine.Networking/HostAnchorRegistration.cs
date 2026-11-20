using System.Runtime.CompilerServices;
namespace BEngine.Hosting;
internal static class PackageHostAnchorRegistration
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255")]
    internal static void Register() => HostAssemblyRegistry.RegisterAnchor(typeof(global::BEngine.Networking.HttpNetworkRequest));
}
