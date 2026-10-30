using BEngine.ProjectSystem;
using Mono.Cecil;

namespace BEngine.Editor;

/// <summary>Font rasterizer included in a Web Player.</summary>
public enum WebFontBackend
{
    /// <summary>Complete Skia text rendering, including COLRv1 and SVG fonts.</summary>
    Skia = 0,
    /// <summary>Compact FreeType text rendering for outline, bitmap and COLRv0 fonts.</summary>
    FreeType = 1
}

internal static class WebFontBackendSettings
{
    internal const string Define = "BENGINE_WEB_FONTS";

    internal static IReadOnlyList<string> PlayerDefines(IEnumerable<string> symbols, bool web, WebFontBackend backend)
    {
        // This symbol describes the selected Player backend, so stale or user-added
        // values must not select it on desktop or after switching back to Skia.
        var result = symbols.Where(static symbol => symbol != Define).ToHashSet(StringComparer.Ordinal);
        if (web && backend == WebFontBackend.FreeType) result.Add(Define);
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    internal static RuntimeManagedCodeReleaseInputSet FilterRuntimeInputs(
        RuntimeManagedCodeReleaseInputSet inputs, bool web, WebFontBackend backend,
        bool deferReferenceValidation = false)
    {
        if (!web || backend != WebFontBackend.FreeType) return inputs;
        // These portable snapshots are normally reused through static Host
        // registration. The compact Host does not register Skia, so publishing
        // its snapshots would instead load unused original DLLs and their
        // unlinked framework/native dependencies in the interpreter.
        var assemblies = inputs.Assemblies.Where(static input => !IsSkiaBinding(input.Name))
            .Select(static input => input with
            {
                Dependencies = input.Dependencies.Where(static dependency => !IsSkiaBinding(dependency)).ToArray()
            }).ToArray();
        var filtered = new RuntimeManagedCodeReleaseInputSet(assemblies, inputs.PackageResources, inputs.PackageResourceRoots);
        if (!deferReferenceValidation) ValidateRuntimeInputs(filtered, web, backend);
        return filtered;
    }

    internal static void ValidateRuntimeInputs(RuntimeManagedCodeReleaseInputSet inputs, bool web, WebFontBackend backend)
    {
        if (!web || backend != WebFontBackend.FreeType) return;
        foreach (var input in inputs.Assemblies.Where(static input =>
                     !DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(input.Name)))
        {
            using var assembly = AssemblyDefinition.ReadAssembly(input.AssemblyPath);
            foreach (var type in assembly.MainModule.GetTypeReferences())
                if (type.GetElementType().Scope is AssemblyNameReference scope && IsSkiaBinding(scope.Name))
                    throw Unsupported(input.Name, type.FullName);
            foreach (var type in assembly.MainModule.ExportedTypes)
            {
                var outer = type;
                while (outer.DeclaringType is { } parent) outer = parent;
                if (outer.Scope is AssemblyNameReference scope && IsSkiaBinding(scope.Name))
                    throw Unsupported(input.Name, type.FullName);
            }
        }
    }

    private static bool IsSkiaBinding(string name) => name.Equals("SkiaSharp", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("SkiaSharp.HarfBuzz", StringComparison.OrdinalIgnoreCase);

    private static InvalidDataException Unsupported(string module, string type) => new(
        $"Web FreeType cannot publish module '{module}' because its runtime contract references Skia type '{type}'. " +
        "Use Skia in Player Settings > Web Font Backend, or remove the Skia dependency.");
}
