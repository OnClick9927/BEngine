using System.Runtime.CompilerServices;
namespace BEngine.Editor.Build;
internal static class PlayerCommandLineRegistration
{
 [ModuleInitializer]
 internal static void Register()
 {
  EditorCommandExtensions.Requested += PlayerCommandLine.TryRun;
  RuntimeAssemblyCleanup.Register(static assembly =>
  {
   if (assembly == typeof(PlayerCommandLineRegistration).Assembly) EditorCommandExtensions.Requested -= PlayerCommandLine.TryRun;
  });
 }
}
