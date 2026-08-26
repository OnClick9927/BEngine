using BEngine.ProjectSystem;
using BEngine.Serialization;

namespace BEngine.Launcher;

internal sealed class LauncherHistoryStore(string path)
{
    private const int MaximumEntries = 50;

    internal string Path { get; } = System.IO.Path.GetFullPath(path);

    internal LauncherSettingsData Load()
    {
        if (!File.Exists(Path)) return new LauncherSettingsData();

        var settings = YamlUtility.Load<LauncherSettingsData>(Path);
        if (!settings.Format.Equals("BEngine.LauncherSettings", StringComparison.Ordinal) ||
            settings.Version is < 1 or > 2)
            throw new InvalidDataException(
                $"Unsupported launcher settings '{settings.Format}' v{settings.Version}.");

        var needsMigration = settings.Version != 2 || HasMovedProject(settings.LastProjectDirectory) ||
                             settings.Projects?.Any(project => HasMovedProject(project.Path)) == true;
        settings.Projects ??= [];
        if (settings.Projects.Count == 0 && IsAbsolute(settings.LastProjectDirectory))
        {
            settings.Projects.Add(CreateRecord(settings.LastProjectDirectory, DateTimeOffset.MinValue));
        }

        settings.Version = 2;
        settings.Projects = settings.Projects
            .Where(project => IsAbsolute(project.Path))
            .Select(Normalize)
            .GroupBy(project => project.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(ParseLastOpened).First())
            .OrderByDescending(ParseLastOpened)
            .Take(MaximumEntries)
            .ToList();
        settings.LastProjectDirectory = settings.Projects.FirstOrDefault()?.Path ?? string.Empty;
        if (needsMigration) Save(settings);
        return settings;
    }

    internal LauncherSettingsData Remember(LauncherSettingsData settings, string projectPath, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var fullPath = System.IO.Path.GetFullPath(projectPath);
        settings.Projects.RemoveAll(project => PathsEqual(project.Path, fullPath));
        settings.Projects.Insert(0, new LauncherProjectRecord
        {
            Name = string.IsNullOrWhiteSpace(name) ? ResolveProjectName(fullPath) : name.Trim(),
            Path = fullPath,
            LastOpenedUtc = DateTimeOffset.UtcNow.ToString("O")
        });
        if (settings.Projects.Count > MaximumEntries)
            settings.Projects.RemoveRange(MaximumEntries, settings.Projects.Count - MaximumEntries);
        settings.Format = "BEngine.LauncherSettings";
        settings.Version = 2;
        settings.LastProjectDirectory = fullPath;
        Save(settings);
        return settings;
    }

    internal void Remove(LauncherSettingsData settings, string projectPath)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var fullPath = System.IO.Path.GetFullPath(projectPath);
        settings.Projects.RemoveAll(project => PathsEqual(project.Path, fullPath));
        if (PathsEqual(settings.LastProjectDirectory, fullPath))
            settings.LastProjectDirectory = settings.Projects.FirstOrDefault()?.Path ?? string.Empty;
        Save(settings);
    }

    internal void Save(LauncherSettingsData settings) => YamlUtility.Save(settings, Path);

    private static LauncherProjectRecord Normalize(LauncherProjectRecord record)
    {
        var fullPath = ResolveMovedProjectPath(record.Path);
        return new LauncherProjectRecord
        {
            Name = string.IsNullOrWhiteSpace(record.Name) ? ResolveProjectName(fullPath) : record.Name.Trim(),
            Path = fullPath,
            LastOpenedUtc = DateTimeOffset.TryParse(record.LastOpenedUtc, out var opened)
                ? opened.ToUniversalTime().ToString("O")
                : string.Empty
        };
    }

    private static LauncherProjectRecord CreateRecord(string path, DateTimeOffset opened) => new()
    {
        Name = ResolveProjectName(path),
        Path = ResolveMovedProjectPath(path),
        LastOpenedUtc = opened == DateTimeOffset.MinValue ? string.Empty : opened.ToUniversalTime().ToString("O")
    };

    private static string ResolveProjectName(string path)
    {
        try
        {
            var projectFile = System.IO.Path.Combine(path, ProjectWorkspace.ProjectFileName);
            if (File.Exists(projectFile)) return ProjectWorkspace.Open(path).Project.Name;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
        }

        return new DirectoryInfo(path).Name;
    }

    private static DateTimeOffset ParseLastOpened(LauncherProjectRecord record) =>
        DateTimeOffset.TryParse(record.LastOpenedUtc, out var opened) ? opened : DateTimeOffset.MinValue;

    private static bool IsAbsolute(string? path) =>
        !string.IsNullOrWhiteSpace(path) && System.IO.Path.IsPathFullyQualified(path);

    private static bool HasMovedProject(string? path) => IsAbsolute(path) &&
        !System.IO.Path.GetFullPath(path!).Equals(ResolveMovedProjectPath(path!), StringComparison.OrdinalIgnoreCase);

    private static string ResolveMovedProjectPath(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        if (File.Exists(System.IO.Path.Combine(fullPath, ProjectWorkspace.ProjectFileName))) return fullPath;
        var legacy = new DirectoryInfo(fullPath);
        if (legacy.Parent is not { } outputDirectory ||
            !outputDirectory.Name.Equals("Output", StringComparison.OrdinalIgnoreCase) ||
            outputDirectory.Parent is not { } repositoryRoot) return fullPath;
        var candidate = System.IO.Path.Combine(repositoryRoot.FullName, legacy.Name);
        return File.Exists(System.IO.Path.Combine(candidate, ProjectWorkspace.ProjectFileName)) ? candidate : fullPath;
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (!IsAbsolute(left) || !IsAbsolute(right)) return false;
        return System.IO.Path.GetFullPath(left!).TrimEnd(System.IO.Path.DirectorySeparatorChar)
            .Equals(System.IO.Path.GetFullPath(right!).TrimEnd(System.IO.Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }
}
