namespace BEngine.Editor;

internal static class BPackageMenuCommands
{
    [MenuItem("Assets/Import Package...", false, 1100)]
    private static void ImportPackage()
    {
        EditorFileDialog.Open("Import BEngine Package", EditorApplication.projectPath,
            "BEngine Package|*.bpackage", ImportSelectedPackage);
    }

    [MenuItem("Assets/Export Package...", false, 1101)]
    private static void ExportPackage()
    {
        var paths = SelectedAssetPaths();
        if (paths.Length == 0) return;
        var defaultName = $"{Path.GetFileName(paths[0].TrimEnd('/', '\\'))}.bpackage";
        EditorFileDialog.Save("Export BEngine Package", EditorApplication.projectPath,
            "BEngine Package|*.bpackage", defaultName, destination => ExportSelectedPackage(paths, destination));
    }

    [MenuItem("Assets/Export Package...", true)]
    private static bool ValidateExportPackage() => SelectedAssetPaths().Length > 0;

    private static void ImportSelectedPackage(string archivePath)
    {
        try
        {
            var result = AssetDatabase.ImportPackage(archivePath,
                new BPackageImportOptions { ConflictPolicy = BPackageConflictPolicy.Fail }, ReportProgress);
            Debug.Log($"Imported {result.Manifest.Name} ({result.ImportedPaths.Count} asset entries).");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static void ExportSelectedPackage(string[] paths, string destination)
    {
        try
        {
            var manifest = AssetDatabase.ExportPackage(paths, EnsureExtension(destination),
                new BPackageExportOptions
                {
                    Name = Path.GetFileNameWithoutExtension(destination),
                    Description = $"Exported from {Application.productName}."
                }, ReportProgress);
            Debug.Log($"Exported {manifest.Name} ({manifest.Entries.Count} entries) to {destination}.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static string[] SelectedAssetPaths() => Selection.objects
        .Select(AssetDatabase.GetAssetPath)
        .Where(path => path.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string EnsureExtension(string destination) =>
        destination.EndsWith(".bpackage", StringComparison.OrdinalIgnoreCase)
            ? destination
            : destination + ".bpackage";

    private static void ReportProgress(BPackageProgress progress) =>
        EditorUtility.DisplayProgressBar("BEngine Package", $"{progress.Phase}: {progress.Path}", progress.Fraction);
}
