namespace BEngine.Launcher;

internal sealed record LauncherPackageDependency(string PackageId, string Source, string Target, bool Optional);
