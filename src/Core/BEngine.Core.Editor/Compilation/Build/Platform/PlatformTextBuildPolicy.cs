using System.Collections.Frozen;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using BEngine.Build;

namespace BEngine.Editor;

/// <summary>Native text bindings, compiler symbols and contract validation for platform publication.</summary>
public sealed class PlatformTextBuildPolicy
{
    private const string FreeTypeDefine = "BENGINE_WEB_FONTS";
    private static readonly PlatformTextBuildPolicy Standard = new(false);
    private static readonly PlatformTextBuildPolicy CompactWeb = new(true);
    private static readonly Regex ReflectionTypeNames = new(
        @"(?:SkiaSharp|HarfBuzzSharp)\.(?:[A-Za-z_][A-Za-z0-9_`]*\.)*[A-Za-z_][A-Za-z0-9_`]*(?:\+[A-Za-z_][A-Za-z0-9_`]*)*",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private readonly bool _compactWeb;

    private PlatformTextBuildPolicy(bool compactWeb) => _compactWeb = compactWeb;

    public static IReadOnlyDictionary<string, string> BindingAnchors { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SkiaSharp"] = "SkiaSharp.SKFont",
            ["SkiaSharp.HarfBuzz"] = "SkiaSharp.HarfBuzz.SKShaper",
            ["HarfBuzzSharp"] = "HarfBuzzSharp.Blob"
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static PlatformTextBuildPolicy For(BuildTargetPlatform platform, WebFontBackend backend) =>
        platform == BuildTargetPlatform.Web && backend == WebFontBackend.FreeType ? CompactWeb : Standard;

    public IReadOnlyList<string> GetDefineSymbols(IEnumerable<string> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        var result = symbols.Where(static symbol => symbol != FreeTypeDefine).ToHashSet(StringComparer.Ordinal);
        if (_compactWeb) result.Add(FreeTypeDefine);
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    public bool IncludesBinding(string assemblyName, bool textBindingsEnabled = true) =>
        textBindingsEnabled ? !_compactWeb || !IsSkiaBinding(assemblyName) : !IsBindingAssembly(assemblyName);

    public static bool IsBindingAssembly(string assemblyName) => BindingAnchors.ContainsKey(assemblyName);

    public static bool ReferencesBindings(string value) =>
        value.Contains("SkiaSharp", StringComparison.Ordinal) || value.Contains("HarfBuzzSharp", StringComparison.Ordinal);

    public static IEnumerable<(string AssemblyName, string TypeName)> FindReflectedTypes(string value)
    {
        foreach (Match match in ReflectionTypeNames.Matches(value))
        {
            var name = match.Value;
            var owner = name.StartsWith("SkiaSharp.HarfBuzz.", StringComparison.Ordinal) ? "SkiaSharp.HarfBuzz" :
                name.StartsWith("HarfBuzzSharp.", StringComparison.Ordinal) ? "HarfBuzzSharp" : "SkiaSharp";
            yield return (owner, name);
        }
    }

    /// <summary>Checks surviving non-host images, after linking when a canonical build discards dead contracts.</summary>
    public void ValidateRuntimeContracts(IEnumerable<string> assemblyPaths)
    {
        ArgumentNullException.ThrowIfNull(assemblyPaths);
        if (!_compactWeb) return;
        foreach (var path in assemblyPaths)
        {
            using var stream = File.OpenRead(path);
            using var image = new PEReader(stream);
            var metadata = image.GetMetadataReader();
            var module = metadata.GetString(metadata.GetAssemblyDefinition().Name);
            foreach (var handle in metadata.TypeReferences)
            {
                var type = metadata.GetTypeReference(handle);
                var scope = type.ResolutionScope;
                while (scope.Kind == HandleKind.TypeReference)
                    scope = metadata.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;
                if (IsExcludedAssembly(metadata, scope)) throw Unsupported(module, TypeName(metadata, handle));
            }
            foreach (var handle in metadata.ExportedTypes)
            {
                var type = metadata.GetExportedType(handle);
                var scope = type.Implementation;
                while (scope.Kind == HandleKind.ExportedType)
                    scope = metadata.GetExportedType((ExportedTypeHandle)scope).Implementation;
                if (IsExcludedAssembly(metadata, scope)) throw Unsupported(module, ExportedName(metadata, handle));
            }
        }
    }

    private static bool IsExcludedAssembly(MetadataReader metadata, EntityHandle scope) =>
        scope.Kind == HandleKind.AssemblyReference &&
        IsSkiaBinding(metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)scope).Name));

    private static bool IsSkiaBinding(string name) => name.Equals("SkiaSharp", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("SkiaSharp.HarfBuzz", StringComparison.OrdinalIgnoreCase);

    private static string TypeName(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var type = metadata.GetTypeReference(handle);
        var name = metadata.GetString(type.Name);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            return TypeName(metadata, (TypeReferenceHandle)type.ResolutionScope) + "/" + name;
        var space = metadata.GetString(type.Namespace);
        return space.Length == 0 ? name : space + "." + name;
    }

    private static string ExportedName(MetadataReader metadata, ExportedTypeHandle handle)
    {
        var type = metadata.GetExportedType(handle);
        var name = metadata.GetString(type.Name);
        if (type.Implementation.Kind == HandleKind.ExportedType)
            return ExportedName(metadata, (ExportedTypeHandle)type.Implementation) + "/" + name;
        var space = metadata.GetString(type.Namespace);
        return space.Length == 0 ? name : space + "." + name;
    }

    private static InvalidDataException Unsupported(string module, string type) => new(
        $"Web FreeType cannot publish module '{module}' because its runtime contract references Skia type '{type}'. " +
        "Use Skia in Player Settings > Web Font Backend, or remove the Skia dependency.");
}
