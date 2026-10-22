using BEngine.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace BEngine.Editor.Codex;

public sealed class CodexProjectStore
{
    private readonly string _legacySettingsPath;

    public string ProjectRoot { get; }
    public string SettingsPath { get; }
    public string SessionPath { get; }

    public CodexProjectStore(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ProjectRoot = EditorHostServices.Paths.GetFullPath(projectRoot);
        SettingsPath = EditorHostServices.Paths.Combine(EditorDataPaths.preferencesDirectoryPath, "CodexSettings.yaml");
        _legacySettingsPath = EditorHostServices.Paths.Combine(ProjectRoot, "ProjectSettings", "CodexSettings.yaml");
        SessionPath = EditorHostServices.Paths.Combine(ProjectRoot, "Library", "Codex", "Session.yaml");
    }

    public CodexProjectSettingsData LoadSettings()
    {
        MigrateLegacySettings();
        if (!EditorHostServices.FileSystem.FileExists(SettingsPath))
        {
            var defaults = new CodexProjectSettingsData();
            SaveSettings(defaults);
            return defaults;
        }

        var settings = YamlUtility.Load<CodexProjectSettingsData>(SettingsPath);
        Validate(settings.Format, settings.Version, "BEngine.CodexSettings", SettingsPath);
        if (CodexProtocolSettings.Normalize(settings)) SaveSettings(settings);
        return settings;
    }

    public CodexSessionDocument LoadSession()
    {
        if (!EditorHostServices.FileSystem.FileExists(SessionPath)) return new CodexSessionDocument();
        var session = YamlUtility.Load<CodexSessionDocument>(SessionPath);
        Validate(session.Format, session.Version, "BEngine.CodexSession", SessionPath);
        session.Transcript ??= [];
        return session;
    }

    public void SaveSettings(CodexProjectSettingsData settings)
    {
        MigrateLegacySettings();
        CodexProtocolSettings.Normalize(settings);
        settings.Save(SettingsPath);
    }

    public void SaveSession(CodexSessionDocument session)
    {
        const int maximumEntries = 250;
        if (session.Transcript.Count > maximumEntries)
        {
            session.Transcript = session.Transcript.Skip(session.Transcript.Count - maximumEntries).ToList();
        }
        session.Save(SessionPath);
    }

    public string NormalizeContextPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = EditorHostServices.Paths.IsPathRooted(path)
            ? EditorHostServices.Paths.GetFullPath(path)
            : EditorHostServices.Paths.GetFullPath(EditorHostServices.Paths.Combine(ProjectRoot, path.Replace('/', EditorHostServices.Paths.DirectorySeparatorChar)));
        var rootWithSeparator = ProjectRoot.EndsWith(EditorHostServices.Paths.DirectorySeparatorChar)
            ? ProjectRoot
            : ProjectRoot + EditorHostServices.Paths.DirectorySeparatorChar;
        if (!fullPath.Equals(ProjectRoot, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Codex context path escapes the project: {path}");
        }
        if (!EditorHostServices.FileSystem.FileExists(fullPath) && !EditorHostServices.FileSystem.DirectoryExists(fullPath))
        {
            throw new FileNotFoundException("Codex context path does not exist.", fullPath);
        }
        return EditorHostServices.Paths.GetRelativePath(ProjectRoot, fullPath).Replace('\\', '/');
    }

    private static void Validate(string format, int version, string expectedFormat, string path)
    {
        if (format != expectedFormat || version != 1)
        {
            throw new InvalidDataException($"Unsupported YAML document '{format}' v{version}: {path}");
        }
    }

    private void MigrateLegacySettings()
    {
        MigrateLegacyGlobalSettings();
        if (!EditorHostServices.FileSystem.FileExists(_legacySettingsPath)) return;
        EditorHostServices.FileSystem.CreateDirectory(EditorHostServices.Paths.GetDirectoryName(SettingsPath)!);
        var destination = EditorHostServices.FileSystem.FileExists(SettingsPath)
            ? CreateConflictBackupPath()
            : SettingsPath;
        if (!EditorHostServices.FileSystem.FileExists(destination)) EditorHostServices.FileSystem.CopyFile(_legacySettingsPath, destination, overwrite: false);
        else if (!FilesHaveSameContents(_legacySettingsPath, destination))
            throw new IOException($"Codex settings migration destination already exists: {destination}");
        try { EditorHostServices.FileSystem.DeleteFile(_legacySettingsPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void MigrateLegacyGlobalSettings()
    {
        var source = EditorHostServices.Paths.Combine(EditorHostServices.Platform.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BEngine", "CodexSettings.yaml");
        if (!EditorHostServices.FileSystem.FileExists(source)) return;
        EditorHostServices.FileSystem.CreateDirectory(EditorHostServices.Paths.GetDirectoryName(SettingsPath)!);
        var destination = EditorHostServices.FileSystem.FileExists(SettingsPath) ? CreateConflictBackupPath() : SettingsPath;
        if (!EditorHostServices.FileSystem.FileExists(destination)) EditorHostServices.FileSystem.CopyFile(source, destination, false);
        if (FilesHaveSameContents(source, destination)) { try { EditorHostServices.FileSystem.DeleteFile(source); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    private string CreateConflictBackupPath()
    {
        var directory = EditorHostServices.Paths.Combine(EditorHostServices.Paths.GetDirectoryName(SettingsPath)!, "MigratedLegacy");
        EditorHostServices.FileSystem.CreateDirectory(directory);
        var projectName = SanitizeFileName(EditorHostServices.Paths.GetFileName(
            EditorHostServices.Paths.TrimEndingDirectorySeparator(ProjectRoot)));
        var normalizedRoot = EditorHostServices.Paths.GetFullPath(ProjectRoot).TrimEnd(EditorHostServices.Paths.DirectorySeparatorChar)
            .ToUpperInvariant();
        var projectHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot)))[..12];
        var prefix = $"CodexSettings-{projectName}-{projectHash}";
        var candidate = EditorHostServices.Paths.Combine(directory, prefix + ".yaml");
        for (var suffix = 2; EditorHostServices.FileSystem.FileExists(candidate) &&
                             !FilesHaveSameContents(_legacySettingsPath, candidate); suffix++)
            candidate = EditorHostServices.Paths.Combine(directory, $"{prefix}-{suffix}.yaml");
        return candidate;
    }

    private static bool FilesHaveSameContents(string left, string right)
    {
        return EditorHostServices.FileSystem.GetFileLength(left) ==
               EditorHostServices.FileSystem.GetFileLength(right) &&
               EditorHostServices.FileSystem.ReadAllBytes(left).AsSpan().SequenceEqual(EditorHostServices.FileSystem.ReadAllBytes(right));
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = EditorHostServices.Paths.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(name.Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray()).Trim(' ', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? "Project" : sanitized;
    }
}
