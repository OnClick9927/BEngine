using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.ProjectSystem;

namespace BEngine.Editor;

/// <summary>Editor registrations follow the same assembly owner as runtime registrations.
/// Project and package scope owners retire them through RuntimeAssemblyCleanup.</summary>
internal static class EditorAssemblyCleanup
{
    [ModuleInitializer]
    internal static void Register() => RuntimeAssemblyCleanup.Register(ForgetAssembly);

    private static void ForgetAssembly(Assembly owner)
    {
        List<Exception>? failures = null;
        Try(() => EditorInitialization.ForgetAssemblies([owner]));
        Try(() => TypeCache.ForgetAssembly(owner));
        Try(() => PackageEditorTemplates.UnregisterAssembly(owner));
        Try(() => ScriptSourceProcessors.UnregisterAssembly(owner));
        Try(() => RuntimeBuildResources.UnregisterAssembly(owner));
        // Release providers before notifying listeners, after the other owned
        // editor registrations have been removed. No provider is created here.
        Try(() => SettingsProviderRegistry.ForgetAssemblies([owner]));
        if (failures is { Count: > 0 })
            throw new AggregateException("Editor assembly registration cleanup failed.", failures);

        void Try(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception failure) { (failures ??= []).Add(failure); }
        }
    }
}
