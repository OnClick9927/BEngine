namespace BEngine.Platform;

/// <summary>Preserves the existing YAML file location and atomic replacement on filesystem platforms.</summary>
public sealed class FilePlayerPrefsStorage : IPlayerPrefsStorage
{
    private FileUtility? _files;
    private PathUtility? _paths;
    private object _identity = new();

    public object Identity
    {
        get
        {
            var files = FileUtility.Current;
            var paths = PathUtility.Current;
            if (!ReferenceEquals(files, _files) || !ReferenceEquals(paths, _paths))
            {
                _files = files;
                _paths = paths;
                _identity = new();
            }
            return _identity;
        }
    }

    private static string Resolve(PlayerPrefsScope scope) =>
        PathUtility.Current.GetFullPath(PathUtility.Current.Combine(scope.PersistentDataPath, "PlayerPrefs.yaml"));

    public string? Read(PlayerPrefsScope scope)
    {
        var path = Resolve(scope);
        var files = FileUtility.Current;
        return files.FileExists(path) ? files.ReadAllText(path) : null;
    }

    public void Write(PlayerPrefsScope scope, string contents)
    {
        var path = Resolve(scope);
        var files = FileUtility.Current;
        var paths = PathUtility.Current;
        var directory = paths.GetDirectoryName(path) ?? PlatformUtility.Current.CurrentDirectory;
        files.CreateDirectory(directory);
        var temporary = paths.Combine(directory, $".{paths.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            files.WriteAllText(temporary, contents);
            files.MoveFile(temporary, path, overwrite: true);
        }
        finally
        {
            if (files.FileExists(temporary)) files.DeleteFile(temporary);
        }
    }
}
