using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor;

public sealed class PlayerBuildProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Default";
    public PlayerBuildSettings Settings { get; set; } = new();
}

public sealed class PlayerBuildProfilesDocument
{
    public const string CurrentFormat = "BEngine.BuildProfiles";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = CurrentFormat;
    public int Version { get; set; } = CurrentVersion;
    public string ActiveProfileId { get; set; } = string.Empty;
    public List<PlayerBuildProfile> Profiles { get; set; } = [];
}

public static class PlayerBuildProfileStore
{
    public const string FileName = "BuildProfiles.yaml";
    public static event Action<string>? profilesChanged;

    private static FileUtility Files => EditorServiceAccess.GetRequiredService<FileUtility>();
    private static PathUtility Paths => EditorServiceAccess.GetRequiredService<PathUtility>();

    public static PlayerBuildProfilesDocument Load(string projectPath)
    {
        var workspace = ProjectWorkspace.Open(projectPath, Files, Paths);
        var path = Paths.Combine(workspace.ProjectSettingsPath, FileName);
        var document = Files.FileExists(path)
            ? YamlUtility.Deserialize<PlayerBuildProfilesDocument>(Files.ReadAllText(path))
            : CreateDefault(projectPath);
        Normalize(projectPath, document);
        return document;
    }

    public static PlayerBuildProfilesDocument Ensure(string projectPath)
    {
        var document = Load(projectPath);
        Write(projectPath, document);
        return document;
    }

    public static PlayerBuildProfile Create(string projectPath, string name, bool duplicateActive = true)
    {
        var document = Load(projectPath);
        var settings = duplicateActive
            ? Clone(Active(document).Settings)
            : Clone(PlayerBuildSettingsStore.Load(projectPath));
        var profile = new PlayerBuildProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = NormalizeName(name),
            Settings = settings
        };
        EnsureUniqueName(document, profile.Name);
        document.Profiles.Add(profile);
        document.ActiveProfileId = profile.Id;
        PersistActive(projectPath, document);
        return Clone(profile);
    }

    public static PlayerBuildProfile SetActive(string projectPath, string profileId)
    {
        var document = Load(projectPath);
        var profile = document.Profiles.FirstOrDefault(item =>
                          item.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase)) ??
                      throw new KeyNotFoundException($"Build profile '{profileId}' was not found.");
        document.ActiveProfileId = profile.Id;
        PersistActive(projectPath, document);
        return Clone(profile);
    }

    public static void Rename(string projectPath, string profileId, string name)
    {
        var document = Load(projectPath);
        var profile = Find(document, profileId);
        var normalized = NormalizeName(name);
        EnsureUniqueName(document, normalized, profile.Id);
        profile.Name = normalized;
        Write(projectPath, document);
        RaiseChanged(projectPath);
    }

    public static PlayerBuildProfile Delete(string projectPath, string profileId)
    {
        var document = Load(projectPath);
        if (document.Profiles.Count == 1)
            throw new InvalidOperationException("The last build profile cannot be deleted.");
        var profile = Find(document, profileId);
        document.Profiles.Remove(profile);
        if (document.ActiveProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))
            document.ActiveProfileId = document.Profiles[0].Id;
        PersistActive(projectPath, document);
        return Clone(Active(document));
    }

    internal static void UpdateActiveSettings(string projectPath, PlayerBuildSettings settings)
    {
        var workspace = ProjectWorkspace.Open(projectPath, Files, Paths);
        var path = Paths.Combine(workspace.ProjectSettingsPath, FileName);
        if (!Files.FileExists(path)) return;
        var document = Load(projectPath);
        Active(document).Settings = Clone(settings);
        Write(projectPath, document);
        RaiseChanged(projectPath);
    }

    public static bool ApplyActiveScriptingDefines(string projectPath)
    {
        var active = Active(Load(projectPath));
        var symbols = active.Settings.ScriptingDefineSymbols;
        if (!projectPath.Equals(EditorApplication.projectPath, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(EditorProjectSettings.settingsPath)) return false;
        if (EditorProjectSettings.current.ScriptingDefineSymbols.SequenceEqual(symbols, StringComparer.Ordinal))
            return false;
        EditorProjectSettings.current.ScriptingDefineSymbols = symbols.ToList();
        EditorProjectSettings.Save();
        CompilationPipeline.RequestScriptCompilation();
        return true;
    }

    public static PlayerBuildProfile GetActive(string projectPath) =>
        Clone(Active(Load(projectPath)));

    private static PlayerBuildProfilesDocument CreateDefault(string projectPath)
    {
        var profile = new PlayerBuildProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Default",
            Settings = Clone(PlayerBuildSettingsStore.Load(projectPath))
        };
        return new PlayerBuildProfilesDocument
        {
            ActiveProfileId = profile.Id,
            Profiles = [profile]
        };
    }

    private static void PersistActive(string projectPath, PlayerBuildProfilesDocument document)
    {
        Normalize(projectPath, document);
        Write(projectPath, document);
        PlayerBuildSettingsStore.SaveFromProfile(projectPath, Clone(Active(document).Settings));
        RaiseChanged(projectPath);
    }

    private static void Normalize(string projectPath, PlayerBuildProfilesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.Format.Equals(PlayerBuildProfilesDocument.CurrentFormat, StringComparison.Ordinal) ||
            document.Version != PlayerBuildProfilesDocument.CurrentVersion)
            throw new InvalidDataException(
                $"Unsupported {FileName} format '{document.Format}' version {document.Version}.");
        if (document.Profiles is null || document.Profiles.Count == 0)
            throw new InvalidDataException("Build Profiles must contain at least one profile.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in document.Profiles)
        {
            if (!Guid.TryParseExact(profile.Id, "N", out _))
                throw new InvalidDataException($"Build profile id '{profile.Id}' is invalid.");
            if (!ids.Add(profile.Id))
                throw new InvalidDataException($"Duplicate build profile id '{profile.Id}'.");
            profile.Name = NormalizeName(profile.Name);
            if (!names.Add(profile.Name))
                throw new InvalidDataException($"Duplicate build profile name '{profile.Name}'.");
            profile.Settings ??= new PlayerBuildSettings();
            PlayerBuildSettingsStore.Normalize(projectPath, profile.Settings);
        }
        if (!ids.Contains(document.ActiveProfileId))
            document.ActiveProfileId = document.Profiles[0].Id;
    }

    private static string NormalizeName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > 64 ||
            normalized.Any(character => char.IsControl(character) || character is '/' or '\\'))
            throw new InvalidDataException("Build profile name must contain 1-64 safe characters.");
        return normalized;
    }

    private static void EnsureUniqueName(
        PlayerBuildProfilesDocument document,
        string name,
        string? exceptId = null)
    {
        if (document.Profiles.Any(profile =>
                !profile.Id.Equals(exceptId, StringComparison.OrdinalIgnoreCase) &&
                profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"A build profile named '{name}' already exists.");
    }

    private static PlayerBuildProfile Find(PlayerBuildProfilesDocument document, string id) =>
        document.Profiles.FirstOrDefault(profile =>
            profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ??
        throw new KeyNotFoundException($"Build profile '{id}' was not found.");

    private static PlayerBuildProfile Active(PlayerBuildProfilesDocument document) =>
        Find(document, document.ActiveProfileId);

    private static void Write(string projectPath, PlayerBuildProfilesDocument document)
    {
        var workspace = ProjectWorkspace.Open(projectPath, Files, Paths);
        Files.WriteAllText(Paths.Combine(workspace.ProjectSettingsPath, FileName),
            YamlUtility.Serialize(document));
    }

    private static PlayerBuildSettings Clone(PlayerBuildSettings value) =>
        YamlUtility.Deserialize<PlayerBuildSettings>(YamlUtility.Serialize(value));

    private static PlayerBuildProfile Clone(PlayerBuildProfile value) =>
        YamlUtility.Deserialize<PlayerBuildProfile>(YamlUtility.Serialize(value));

    private static void RaiseChanged(string projectPath)
    {
        var root = ProjectWorkspace.Open(projectPath, Files, Paths).RootPath;
        EditorCallbackDispatcher.Invoke(profilesChanged, root, nameof(profilesChanged));
    }
}
