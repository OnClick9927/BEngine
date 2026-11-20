using System.Runtime.CompilerServices;
namespace BEngine.Editor.Build;
internal static class PlayerBuildProfilePresentation
{
 [ModuleInitializer]
 internal static void Register()
 {
  PlayerBuildProfileStore.applyScriptingDefines += Apply;
  RuntimeAssemblyCleanup.Register(static assembly =>
  {
   if (assembly == typeof(PlayerBuildProfilePresentation).Assembly) PlayerBuildProfileStore.applyScriptingDefines -= Apply;
  });
 }
 private static bool Apply(string projectPath, IReadOnlyList<string> symbols)
 {
  if (!projectPath.Equals(EditorApplication.projectPath,StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(EditorProjectSettings.settingsPath)) return false;
  return EditorScriptingDefines.SetSymbols(symbols);
 }
}
