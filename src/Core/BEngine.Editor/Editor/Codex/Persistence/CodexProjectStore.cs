using BEngine.Documents;
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
        ProjectRoot = Path.GetFullPath(projectRoot);
        SettingsPath = EditorDataPaths.codexSettingsPath;
        _legacySettingsPath = Path.Combine(ProjectRoot, "ProjectSettings", "CodexSettings.yaml");
        SessionPath = Path.Combine(ProjectRoot, "Library", "Codex", "Session.yaml");
    }

    public CodexProjectSettingsData LoadSettings()
    {
        MigrateLegacySettings();
        if (!File.Exists(SettingsPath))
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
        if (!File.Exists(SessionPath)) return new CodexSessionDocument();
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
        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(ProjectRoot, path.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = ProjectRoot.EndsWith(Path.DirectorySeparatorChar)
            ? ProjectRoot
            : ProjectRoot + Path.DirectorySeparatorChar;
        if (!fullPath.Equals(ProjectRoot, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Codex context path escapes the project: {path}");
        }
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("Codex context path does not exist.", fullPath);
        }
        return Path.GetRelativePath(ProjectRoot, fullPath).Replace('\\', '/');
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
        if (!File.Exists(_legacySettingsPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var destination = File.Exists(SettingsPath)
            ? CreateConflictBackupPath()
            : SettingsPath;
        if (!File.Exists(destination)) File.Copy(_legacySettingsPath, destination, overwrite: false);
        else if (!FilesHaveSameContents(_legacySettingsPath, destination))
            throw new IOException($"Codex settings migration destination already exists: {destination}");
        try { File.Delete(_legacySettingsPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string CreateConflictBackupPath()
    {
        var directory = Path.Combine(Path.GetDirectoryName(SettingsPath)!, "MigratedLegacy");
        Directory.CreateDirectory(directory);
        var projectName = SanitizeFileName(Path.GetFileName(
            Path.TrimEndingDirectorySeparator(ProjectRoot)));
        var normalizedRoot = Path.GetFullPath(ProjectRoot).TrimEnd(Path.DirectorySeparatorChar)
            .ToUpperInvariant();
        var projectHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot)))[..12];
        var prefix = $"CodexSettings-{projectName}-{projectHash}";
        var candidate = Path.Combine(directory, prefix + ".yaml");
        for (var suffix = 2; File.Exists(candidate) &&
                             !FilesHaveSameContents(_legacySettingsPath, candidate); suffix++)
            candidate = Path.Combine(directory, $"{prefix}-{suffix}.yaml");
        return candidate;
    }

    private static bool FilesHaveSameContents(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        return leftInfo.Length == rightInfo.Length &&
               File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right));
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(name.Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray()).Trim(' ', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? "Project" : sanitized;
    }
}
