using BEngine.Documents;
using BEngine.Serialization;

namespace BEngine.Editor.Codex;

public sealed class CodexProjectStore
{
    public string ProjectRoot { get; }
    public string SettingsPath { get; }
    public string SessionPath { get; }

    public CodexProjectStore(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ProjectRoot = Path.GetFullPath(projectRoot);
        SettingsPath = Path.Combine(ProjectRoot, "ProjectSettings", "CodexSettings.yaml");
        SessionPath = Path.Combine(ProjectRoot, "Library", "Codex", "Session.yaml");
    }

    public CodexProjectSettingsDocument LoadSettings()
    {
        if (!File.Exists(SettingsPath))
        {
            var defaults = new CodexProjectSettingsDocument();
            SaveSettings(defaults);
            return defaults;
        }

        var settings = Document.Load<CodexProjectSettingsDocument>(SettingsPath);
        Validate(settings.Format, settings.Version, "BEngine.CodexSettings", SettingsPath);
        if (CodexProtocolSettings.Normalize(settings)) SaveSettings(settings);
        return settings;
    }

    public CodexSessionDocument LoadSession()
    {
        if (!File.Exists(SessionPath)) return new CodexSessionDocument();
        var session = Document.Load<CodexSessionDocument>(SessionPath);
        Validate(session.Format, session.Version, "BEngine.CodexSession", SessionPath);
        session.Transcript ??= [];
        return session;
    }

    public void SaveSettings(CodexProjectSettingsDocument settings)
    {
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
}
