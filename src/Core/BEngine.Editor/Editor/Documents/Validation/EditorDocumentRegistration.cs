using System.Runtime.CompilerServices;
using BEngine.Documents;

namespace BEngine.Editor.Documents;

internal static class EditorDocumentRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        DocumentConversionRegistry.Register(AssemblyDefinitionDocumentConverter.Shared);
        DocumentValidationRegistry.Register<EditorSettingsDocument>(ValidateEditorSettings);
        DocumentValidationRegistry.Register<EditorLayoutDocument>(ValidateEditorLayout);
        DocumentValidationRegistry.Register<EditorPreferencesDocument>(ValidateEditorPreferences);
        DocumentValidationRegistry.Register<LauncherSettingsDocument>(ValidateLauncherSettings);
        DocumentValidationRegistry.Register<AssemblyDefinitionDocument>(ValidateAssemblyDefinition);
    }

    private static void ValidateAssemblyDefinition(AssemblyDefinitionDocument document)
    {
        if (document.Format != "BEngine.AssemblyDefinition" || document.Version != 1)
            throw new InvalidDataException(
                $"Unsupported assembly definition '{document.Format}' v{document.Version}.");
        if (string.IsNullOrWhiteSpace(document.Name) ||
            document.Name != document.Name.Trim() ||
            document.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Assembly definition name is invalid.");
        if (!IsNamespace(document.RootNamespace))
            throw new InvalidDataException("Root namespace is invalid.");
        ValidateDistinct(document.References, "reference", document.Name, StringComparer.OrdinalIgnoreCase);
        if (document.References.Contains(document.Name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Assembly definition '{document.Name}' cannot reference itself.");
        ValidateDistinct(document.IncludePlatforms, "included platform", document.Name,
            StringComparer.OrdinalIgnoreCase);
        ValidateDistinct(document.ExcludePlatforms, "excluded platform", document.Name,
            StringComparer.OrdinalIgnoreCase);
        if (document.IncludePlatforms.Count > 0 && document.ExcludePlatforms.Count > 0)
            throw new InvalidDataException(
                "Platforms cannot be both included and excluded; configure only one platform list.");
        ValidateDistinct(document.DefineConstraints, "define constraint", document.Name, StringComparer.Ordinal);
        foreach (var constraint in document.DefineConstraints)
        {
            var symbol = constraint.StartsWith('!') ? constraint[1..] : constraint;
            if (!IsIdentifier(symbol))
                throw new InvalidDataException($"Invalid define constraint '{constraint}'.");
        }
    }

    private static void ValidateDistinct(
        IReadOnlyCollection<string> values,
        string kind,
        string assemblyName,
        StringComparer comparer)
    {
        if (values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException($"Assembly definition '{assemblyName}' contains an empty {kind}.");
        if (values.Select(value => value.Trim()).Distinct(comparer).Count() != values.Count)
            throw new InvalidDataException($"Assembly definition '{assemblyName}' contains a duplicate {kind}.");
    }

    private static bool IsNamespace(string value) => !string.IsNullOrWhiteSpace(value) &&
        value.Split('.').All(IsIdentifier);

    private static bool IsIdentifier(string value) => value.Length > 0 &&
        (char.IsLetter(value[0]) || value[0] == '_') &&
        value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');

    private static void ValidateEditorSettings(EditorSettingsDocument document)
    {
        if (document.Format != "BEngine.EditorSettings" || document.Version != 1)
            throw new InvalidDataException(
                $"Unsupported editor settings '{document.Format}' v{document.Version}.");
    }

    private static void ValidateEditorPreferences(EditorPreferencesDocument document)
    {
        if (document.Format != "BEngine.Preferences" || document.Version != 1)
            throw new InvalidDataException(
                $"Unsupported editor preferences '{document.Format}' v{document.Version}.");
        // Legacy files may contain a different positive size. They are migrated to the
        // fixed editor font size when loaded, while malformed values remain invalid.
        if (document.EditorFontSize <= 0)
            throw new InvalidDataException("Editor font size must be positive.");
        if (!float.IsFinite(document.EditorScale) || document.EditorScale is < 0.75f or > 2f)
            throw new InvalidDataException("Editor scale must be between 0.75 and 2.0.");
        if (string.IsNullOrWhiteSpace(document.Locale) || string.IsNullOrWhiteSpace(document.EditorFont) ||
            string.IsNullOrWhiteSpace(document.EditorTheme))
            throw new InvalidDataException("Editor locale, font, and theme cannot be empty.");
    }

    private static void ValidateLauncherSettings(LauncherSettingsDocument document)
    {
        if (document.Format != "BEngine.LauncherSettings" || document.Version != 1)
            throw new InvalidDataException(
                $"Unsupported launcher settings '{document.Format}' v{document.Version}.");
        if (string.IsNullOrWhiteSpace(document.LastProjectDirectory) ||
            !Path.IsPathFullyQualified(document.LastProjectDirectory))
            throw new InvalidDataException("The last project directory must be an absolute path.");
    }

    private static void ValidateEditorLayout(EditorLayoutDocument document)
    {
        if (document.Format != "BEngine.EditorLayout" || document.Version is < 1 or > 2)
            throw new InvalidDataException(
                $"Unsupported editor layout '{document.Format}' v{document.Version}.");
        if (document.WindowWidth < 640 || document.WindowHeight < 480)
            throw new InvalidDataException("The editor window size is too small.");
        if (!float.IsFinite(document.HierarchyWidth) || !float.IsFinite(document.InspectorWidth) ||
            !float.IsFinite(document.BottomHeight) || !float.IsFinite(document.ProjectFoldersWidth) ||
            !float.IsFinite(document.ProjectThumbnailSize))
            throw new InvalidDataException("Editor layout panel sizes must be finite numbers.");

        float[] values =
        [
            document.SceneCameraPositionX, document.SceneCameraPositionY,
            document.SceneCameraRotation, document.SceneCameraSize,
            document.SceneCameraBackgroundR, document.SceneCameraBackgroundG, document.SceneCameraBackgroundB
        ];
        if (values.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Scene camera settings must be finite numbers.");
        if (document.SceneCameraSize <= 0)
            throw new InvalidDataException("Scene camera settings are outside the supported range.");
        if (document.Version < 2) return;
        if (string.IsNullOrWhiteSpace(document.Name) || string.IsNullOrWhiteSpace(document.ActiveLayout))
            throw new InvalidDataException("Editor layout names cannot be empty.");
        var windows = document.Windows ?? throw new InvalidDataException("Editor layout windows are missing.");
        if (windows.Any(window => string.IsNullOrWhiteSpace(window.Id) ||
                                  string.IsNullOrWhiteSpace(window.TypeName) ||
                                  !Enum.TryParse<EditorWindowState>(window.State, true, out _) ||
                                  !float.IsFinite(window.X) || !float.IsFinite(window.Y) ||
                                  !float.IsFinite(window.Width) || !float.IsFinite(window.Height) ||
                                  window.Width < 1 || window.Height < 1))
            throw new InvalidDataException("Editor layout contains an invalid window record.");
        if (windows.Select(window => window.Id).Distinct(StringComparer.Ordinal).Count() != windows.Count)
            throw new InvalidDataException("Editor layout contains duplicate window IDs.");
        if (document.FocusedWindowId is not null &&
            !windows.Any(window => window.Id == document.FocusedWindowId))
            throw new InvalidDataException("The focused layout window has no window record.");
        ValidateDockNode(document.DockRoot, windows.Select(window => window.Id).ToHashSet(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));
    }

    private static void ValidateDockNode(EditorDockNodeDocument? node, IReadOnlySet<string> windows,
        ISet<string> dockedPanels)
    {
        if (node is null) return;
        if (node.Type.Equals("Group", StringComparison.OrdinalIgnoreCase))
        {
            if (node.First is not null || node.Second is not null)
                throw new InvalidDataException("A dock group cannot contain child nodes.");
            foreach (var panel in node.Panels)
            {
                if (!windows.Contains(panel))
                    throw new InvalidDataException($"Dock panel '{panel}' has no window record.");
                if (!dockedPanels.Add(panel))
                    throw new InvalidDataException($"Dock panel '{panel}' appears more than once.");
            }
            if (node.SelectedId is not null && !node.Panels.Contains(node.SelectedId, StringComparer.Ordinal))
                throw new InvalidDataException("The selected dock panel is not part of its group.");
            return;
        }
        if (!node.Type.Equals("Split", StringComparison.OrdinalIgnoreCase) ||
            !float.IsFinite(node.Ratio) || node.Ratio is <= 0.05f or >= 0.95f ||
            node.First is null || node.Second is null || node.Panels.Count > 0)
            throw new InvalidDataException("Editor layout contains an invalid dock split.");
        ValidateDockNode(node.First, windows, dockedPanels);
        ValidateDockNode(node.Second, windows, dockedPanels);
    }
}
