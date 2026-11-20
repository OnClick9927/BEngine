using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Editor.Documents;
namespace BEngine.ProjectSystem;

public sealed record PackageEditorTemplate(string Source, string[] AssemblyReferences, PackageDependencyDocument[] Dependencies);
public readonly record struct PackageEditorTemplateContext(string Name, string SourceNamespace, string WindowName);
public static class PackageEditorTemplates
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<Assembly, Func<PackageEditorTemplateContext, PackageEditorTemplate>> Providers = [];
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register(Func<PackageEditorTemplateContext, PackageEditorTemplate> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock(Gate) Providers[Assembly.GetCallingAssembly()] = provider;
    }
    /// <summary>Creates source through the active editor template provider, or the headless
    /// menu-command fallback. Naming and destination validation belong to the authoring tool.</summary>
    public static PackageEditorTemplate Create(PackageEditorTemplateContext context)
    {
        Func<PackageEditorTemplateContext, PackageEditorTemplate>? provider;
        lock(Gate) provider = Providers.Values.LastOrDefault();
        return provider is not null ? provider(context) : new($$"""
            using BEngine;
            using BEngine.Editor;
            namespace {{context.SourceNamespace}}.Editor;
            internal static class {{context.WindowName}}
            {
                [MenuItem("Tools/{{context.Name}}")]
                private static void Run() => Debug.Log("{{context.Name}} is ready.");
            }
            """, [], []);
    }
    internal static void UnregisterAssembly(Assembly assembly) { lock(Gate) Providers.Remove(assembly); }
}
