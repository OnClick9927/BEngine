namespace BEngine.Editor;

// Project link.xml files may explicitly scope legacy, broad Host roots to builds
// without the canonical Web ABI. Unconditional user roots always remain intact.
internal static class WebStaticHostContractFeature
{
    internal const string Name = "BEngine.Build.StaticWebHost";

    // A partial canonical link skips Host bodies, so it cannot see their interface
    // casts and reflection. Keep legacy roots here: otherwise ILLink can remove
    // implemented Host interfaces (and their implementation methods) from scripts.
    // Only the final complete Host link may replace the broad project contract.
    internal static bool IsCanonicalPassEnabled(PlayerBuildContext context, bool canonicalPass) => false;

    internal static bool IsHostPassEnabled(PlayerBuildContext context, bool preciseHostRootsReady) =>
        preciseHostRootsReady && context.Request.SelfContained && WebCanonicalScriptBuild.IsEnabled(context) &&
        context.RuntimeManagedCodeAssemblyPaths.Count > 0 && context.WebPrecompiledManagedCodeAssemblyPaths.Count > 0;

    internal static void AddLinkerArguments(ICollection<string> arguments, bool enabled)
    {
        if (!enabled) return;
        arguments.Add("--feature");
        arguments.Add(Name);
        arguments.Add("true");
    }

    internal static string SdkLinkerArguments(bool enabled) => enabled ? " --feature " + Name + " true" : string.Empty;
}
