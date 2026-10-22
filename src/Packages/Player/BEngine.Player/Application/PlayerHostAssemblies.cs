using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using BEngine.HotUpdate;

namespace BEngine.Player;

internal static class PlayerHostAssemblies
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Share the Player host contracts with managed-code releases.")]
    internal static void Initialize()
    {
        var root = typeof(PlayerHostAssemblies).Assembly;
        if (AssemblyLoadContext.GetLoadContext(root) != AssemblyLoadContext.Default) return;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Visit(root);

        void Visit(Assembly assembly)
        {
            if (!visited.Add(assembly.GetName().Name!)) return;
            ManagedCodeHostAssemblies.Register(assembly);
            foreach (var reference in assembly.GetReferencedAssemblies())
                if (reference.Name?.StartsWith("BEngine", StringComparison.Ordinal) == true)
                    Visit(AssemblyLoadContext.Default.LoadFromAssemblyName(reference));
            // Reflection loading alone does not execute package service registrations.
            // Bootstrap every dependency before the Player constructs its first DI provider.
            if (!ReferenceEquals(assembly, root))
                RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        }
    }
}
