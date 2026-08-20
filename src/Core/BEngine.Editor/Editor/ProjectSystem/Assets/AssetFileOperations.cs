namespace BEngine.ProjectSystem.Editor;

internal static class AssetFileOperations
{
    internal static string Copy(string sourcePath, string targetPath)
    {
        try
        {
            var source = Path.GetFullPath(sourcePath);
            var target = Path.GetFullPath(targetPath);
            var sourceIsFile = File.Exists(source);
            if (!sourceIsFile && !Directory.Exists(source)) return $"Asset not found: {source}";
            if (File.Exists(target) || Directory.Exists(target)) return $"Destination already exists: {target}";
            if (Directory.Exists(source) && target.StartsWith(source + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                return "A folder cannot be copied into itself.";

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (sourceIsFile)
            {
                File.Copy(source, target);
                return string.Empty;
            }

            Directory.CreateDirectory(target);
            foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                         .Where(static file => !file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            return string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return exception.Message;
        }
    }

    internal static string Move(string sourcePath, string targetPath)
    {
        try
        {
            var source = Path.GetFullPath(sourcePath);
            var target = Path.GetFullPath(targetPath);
            var sourceIsFile = File.Exists(source);
            if (!sourceIsFile && !Directory.Exists(source)) return $"Asset not found: {source}";
            if (File.Exists(target) || Directory.Exists(target)) return $"Destination already exists: {target}";
            if (Directory.Exists(source) && target.StartsWith(source + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                return "A folder cannot be moved into itself.";

            var sourceMetadata = source + ".meta";
            var targetMetadata = target + ".meta";
            if (File.Exists(sourceMetadata) && File.Exists(targetMetadata))
                return $"Destination metadata already exists: {targetMetadata}";

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (sourceIsFile) File.Move(source, target); else Directory.Move(source, target);
            try
            {
                if (File.Exists(sourceMetadata)) File.Move(sourceMetadata, targetMetadata);
            }
            catch
            {
                if (sourceIsFile) File.Move(target, source); else Directory.Move(target, source);
                throw;
            }

            return string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return exception.Message;
        }
    }
}
