using BEngine.AssetBundles;
using BEngine.Documents;

namespace BEngine.Editor;

internal static class AssetBundleSettingsProvider
{
    private static AssetBundleSettingsDocument _draft = new();
    private static string _settingsPath = string.Empty;
    private static string _loadError = string.Empty;
    private static string _operationError = string.Empty;
    private static string _validationError = string.Empty;
    private static bool _dirty;

    [SettingsProvider]
    private static SettingsProvider Create() => new("Project/Asset Bundles", SettingsScope.Project,
        ["assetbundle", "bundle", "hot update", "remote", "cache", "resource", "资源包", "热更新", "缓存"])
    {
        label = "Asset Bundles",
        activateHandler = _ => EnsureLoaded(),
        guiHandler = _ => Draw(),
        footerBarGuiHandler = DrawFooter
    };

    private static void Draw()
    {
        EnsureLoaded();
        DrawToggle("Enabled", _draft.Enabled, value => _draft.Enabled = value);
        DrawTextField("Package Name", _draft.PackageName, value => _draft.PackageName = value);
        DrawProjectFolderField("Built-in Directory", _draft.BuiltInDirectory,
            "Choose Built-in AssetBundle Directory", value => _draft.BuiltInDirectory = value);
        DrawTextField("Remote Base URL", _draft.RemoteBaseUrl, value => _draft.RemoteBaseUrl = value);
        DrawCacheFolderField();

        GUILayout.Space(8);
        GUILayout.Label("Startup Update", EditorStyles.boldLabel);
        DrawToggle("Check for Updates on Startup", _draft.CheckForUpdatesOnStartup,
            value => _draft.CheckForUpdatesOnStartup = value);
        DrawToggle("Apply Updates on Startup", _draft.ApplyUpdatesOnStartup,
            value => _draft.ApplyUpdatesOnStartup = value);
        DrawToggle("Fail Startup When Update Fails", _draft.FailStartupWhenUpdateFails,
            value => _draft.FailStartupWhenUpdateFails = value);

        GUILayout.Space(8);
        GUILayout.Label("Download", EditorStyles.boldLabel);
        DrawToggle("Require HTTPS", _draft.RequireHttps, value => _draft.RequireHttps = value);
        DrawIntField("Max Retries", _draft.MaxRetries, value => _draft.MaxRetries = value);

        if (_loadError.Length > 0) EditorGUILayout.HelpBox(_loadError, MessageType.Error);
        if (_operationError.Length > 0) EditorGUILayout.HelpBox(_operationError, MessageType.Error);
        if (_validationError.Length > 0) EditorGUILayout.HelpBox(_validationError, MessageType.Error);
        else if (!_draft.CheckForUpdatesOnStartup &&
                 (_draft.ApplyUpdatesOnStartup || _draft.FailStartupWhenUpdateFails))
            EditorGUILayout.HelpBox(
                "Startup apply/failure settings have no effect while startup update checks are disabled.",
                MessageType.Warning);
        else if (_draft.Enabled && string.IsNullOrWhiteSpace(_draft.RemoteBaseUrl))
            EditorGUILayout.HelpBox(
                "No remote base URL is configured. The player can use built-in or cached bundles, but cannot check for hot updates.",
                MessageType.Info);
    }

    private static void DrawFooter()
    {
        EnsureLoaded();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUI.BeginDisabledGroup(!_dirty);
        if (GUILayout.Button("Revert", GUILayout.Width(80))) Reload();
        EditorGUI.EndDisabledGroup();
        EditorGUI.BeginDisabledGroup(!_dirty || _validationError.Length > 0 || _settingsPath.Length == 0);
        if (GUILayout.Button("Apply", GUILayout.Width(80))) Save();
        EditorGUI.EndDisabledGroup();
        GUILayout.EndHorizontal();
    }

    private static void DrawToggle(string label, bool value, Action<bool> assign)
    {
        var next = EditorGUILayout.Toggle(label, value);
        if (next == value) return;
        assign(next);
        Changed();
    }

    private static void DrawTextField(string label, string value, Action<string> assign)
    {
        var next = EditorGUILayout.TextField(label, value ?? string.Empty);
        if (next.Equals(value, StringComparison.Ordinal)) return;
        assign(next);
        Changed();
    }

    private static void DrawIntField(string label, int value, Action<int> assign)
    {
        var next = EditorGUILayout.IntField(label, value);
        if (next == value) return;
        assign(next);
        Changed();
    }

    private static void DrawProjectFolderField(
        string label,
        string value,
        string title,
        Action<string> assign)
    {
        DrawFolderField(label, value, title, EditorApplication.projectPath, assign);
    }

    private static void DrawCacheFolderField()
    {
        var cacheRoot = Application.persistentDataPath;
        DrawFolderField("Cache Directory", _draft.CacheDirectory, "Choose AssetBundle Cache Directory",
            cacheRoot, value => _draft.CacheDirectory = value);
    }

    private static void DrawFolderField(
        string label,
        string value,
        string title,
        string rootPath,
        Action<string> assign)
    {
        var row = GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
        var buttonWidth = Fix64.Min(28, Fix64.Max(20, row.width / 6));
        var gap = (Fix64)4;
        var fieldWidth = Fix64.Max(0, row.width - buttonWidth - gap);
        var next = EditorGUI.TextField(new Rect(row.x, row.y, fieldWidth, row.height), label,
            value ?? string.Empty);
        if (!next.Equals(value, StringComparison.Ordinal))
        {
            assign(next);
            Changed();
        }

        var buttonRect = new Rect(row.x + fieldWidth + gap, row.y, buttonWidth, row.height);
        if (!GUI.Button(buttonRect,
                new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.OpenFolder, title),
                EditorStyles.toolbarIconButton)) return;

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            _operationError = "A project must be open before choosing this directory.";
            return;
        }

        var root = Path.GetFullPath(rootPath);
        var initialDirectory = ResolveInitialDirectory(root, value ?? string.Empty);
        EditorFileDialog.OpenFolder(title, initialDirectory, selected =>
        {
            if (!TryMakeRelativeDirectory(root, selected, out var relative, out var error))
            {
                _operationError = error;
                return;
            }
            assign(relative);
            Changed();
        });
    }

    private static string ResolveInitialDirectory(string root, string value)
    {
        var fallback = NearestExistingDirectory(root);
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try
        {
            var candidate = Path.GetFullPath(Path.Combine(root,
                value.Replace('/', Path.DirectorySeparatorChar)));
            return Directory.Exists(candidate) ? candidate : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string NearestExistingDirectory(string path)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (!current.Exists && current.Parent is not null) current = current.Parent;
        return current.Exists ? current.FullName : EditorApplication.projectPath;
    }

    private static bool TryMakeRelativeDirectory(
        string root,
        string selected,
        out string relative,
        out string error)
    {
        relative = string.Empty;
        error = string.Empty;
        try
        {
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var normalizedSelection = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selected));
            var candidate = Path.GetRelativePath(normalizedRoot, normalizedSelection).Replace('\\', '/');
            if (candidate.Equals(".", StringComparison.Ordinal) || Path.IsPathRooted(candidate) ||
                candidate.Split('/').Any(segment => segment is ".." or "."))
            {
                error = $"Choose a subdirectory inside '{normalizedRoot}'.";
                return false;
            }
            relative = candidate.Trim('/');
            return true;
        }
        catch (Exception exception)
        {
            error = $"The selected directory is invalid: {exception.Message}";
            return false;
        }
    }

    private static void Changed()
    {
        _dirty = true;
        _operationError = string.Empty;
        _validationError = Validate(_draft);
    }

    private static void EnsureLoaded()
    {
        var path = ResolveSettingsPath();
        if (path.Equals(_settingsPath, StringComparison.OrdinalIgnoreCase)) return;
        Reload(path);
    }

    private static void Reload() => Reload(ResolveSettingsPath());

    private static void Reload(string path)
    {
        _settingsPath = path;
        _loadError = string.Empty;
        _operationError = string.Empty;
        try
        {
            _draft = path.Length > 0 && File.Exists(path)
                ? Document.Load<AssetBundleSettingsDocument>(path)
                : new AssetBundleSettingsDocument();
        }
        catch (Exception exception)
        {
            _draft = new AssetBundleSettingsDocument();
            _loadError = $"AssetBundle settings could not be loaded; defaults are shown: {exception.Message}";
        }
        _dirty = false;
        _validationError = Validate(_draft);
    }

    private static void Save()
    {
        try
        {
            var value = NormalizedCopy(_draft);
            var error = Validate(value);
            if (error.Length > 0)
            {
                _validationError = error;
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            value.Save(_settingsPath);
            _draft = value;
            _dirty = false;
            _loadError = string.Empty;
            _operationError = string.Empty;
            _validationError = string.Empty;
            Debug.Log($"Saved AssetBundle project settings to '{_settingsPath}'.");
        }
        catch (Exception exception)
        {
            _operationError = $"AssetBundle settings could not be saved: {exception.Message}";
            Debug.LogException(exception);
        }
    }

    private static string Validate(AssetBundleSettingsDocument value)
    {
        try
        {
            var candidate = NormalizedCopy(value);
            if (candidate.PackageName.Length == 0) return "Package Name is required.";
            if (candidate.PackageName.Length > 128) return "Package Name cannot exceed 128 characters.";
            if (!IsPackageNameCharacter(candidate.PackageName[0], first: true) ||
                candidate.PackageName.Skip(1).Any(character => !IsPackageNameCharacter(character, first: false)))
                return "Package Name may contain letters, digits, '.', '_' and '-', and must start with a letter or digit.";
            if (ValidateRelativePath(candidate.BuiltInDirectory, "Built-in Directory") is { } builtInError)
                return builtInError;
            if (ValidateRelativePath(candidate.CacheDirectory, "Cache Directory") is { } cacheError)
                return cacheError;
            if (candidate.RemoteBaseUrl.Length > 0)
            {
                if (!Uri.TryCreate(candidate.RemoteBaseUrl, UriKind.Absolute, out var remote) ||
                    remote.Scheme is not ("http" or "https"))
                    return "Remote Base URL must be an absolute HTTP or HTTPS URL.";
                if (candidate.RequireHttps && !remote.Scheme.Equals(Uri.UriSchemeHttps,
                        StringComparison.OrdinalIgnoreCase))
                    return "Remote Base URL must use HTTPS while Require HTTPS is enabled.";
            }
            if (candidate.MaxRetries < 0) return "Max Retries must be zero or greater.";
            _ = candidate.ToYaml();
            return string.Empty;
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    private static bool IsPackageNameCharacter(char character, bool first) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' ||
        !first && character is '.' or '_' or '-';

    private static string? ValidateRelativePath(string path, string label)
    {
        if (path.Length == 0) return null;
        if (Path.IsPathRooted(path) || path.Split('/').Any(segment => segment is "." or ".."))
            return $"{label} must be a relative path without '.' or '..' segments.";
        return null;
    }

    private static AssetBundleSettingsDocument NormalizedCopy(AssetBundleSettingsDocument value) => new()
    {
        Format = value.Format,
        Version = value.Version,
        Enabled = value.Enabled,
        PackageName = value.PackageName?.Trim() ?? string.Empty,
        BuiltInDirectory = NormalizeRelativePath(value.BuiltInDirectory),
        RemoteBaseUrl = value.RemoteBaseUrl?.Trim().TrimEnd('/') ?? string.Empty,
        CacheDirectory = NormalizeRelativePath(value.CacheDirectory),
        CheckForUpdatesOnStartup = value.CheckForUpdatesOnStartup,
        ApplyUpdatesOnStartup = value.ApplyUpdatesOnStartup,
        FailStartupWhenUpdateFails = value.FailStartupWhenUpdateFails,
        RequireHttps = value.RequireHttps,
        MaxRetries = value.MaxRetries
    };

    private static string NormalizeRelativePath(string? value) =>
        (value ?? string.Empty).Trim().Replace('\\', '/');

    private static string ResolveSettingsPath()
    {
        if (string.IsNullOrWhiteSpace(EditorApplication.projectPath)) return string.Empty;
        return Path.Combine(Path.GetFullPath(EditorApplication.projectPath), "ProjectSettings",
            AssetBundleSettingsDocument.FileName);
    }
}
