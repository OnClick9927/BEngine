using System.Runtime.CompilerServices;
using BEngine.Platform;
using BEngine.Build;

namespace BEngine.Editor.Build;

internal static class UIElementsPlayerBuildValidation
{
    [ModuleInitializer]
    internal static void Register()
    {
        PlayerBuildValidation.Assets += Validate;
        RuntimeAssemblyCleanup.Register(static assembly =>
        {
            if (assembly == typeof(UIElementsPlayerBuildValidation).Assembly)
                PlayerBuildValidation.Assets -= Validate;
        });
    }

    private static void Validate(PlayerBuildContext context, IEnumerable<string> assetFiles,
        FileUtility files, PathUtility paths, CancellationToken cancellationToken)
    {
        if (context.Target.Platform != BuildTargetPlatform.Web || context.Request.WebFontBackend != WebFontBackend.FreeType)
            return;
        var assets = 0;
        var fonts = 0;
        var faces = 0;
        foreach (var path in assetFiles.Where(path => paths.GetExtension(path).Equals(".sdfont", StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (files.GetFileLength(path) > 192L * 1024 * 1024)
                throw new InvalidDataException($"Font asset '{path}' exceeds the 192 MB asset limit.");
            var result = WebFontCapabilityValidator.ValidateAsset(files.ReadAllBytes(path), path);
            assets++;
            fonts += result.EmbeddedFonts;
            faces += result.Faces;
        }
        Debug.Log($"Web FreeType font capability check: {assets} admitted .sdfont assets, " +
                  $"{fonts} embedded font sources, {faces} SFNT faces.");
    }
}
