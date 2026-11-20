using System.Runtime.CompilerServices;
namespace BootstrapProbe;
public static class Policy
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Exercise discovery of an independently published editor policy.")]
    internal static void Initialize() => AppContext.SetData("BootstrapPolicyInitialized", true);
    public static int Read() => Value.Read();
    public static int ReadLater() => Late.Read();
}
