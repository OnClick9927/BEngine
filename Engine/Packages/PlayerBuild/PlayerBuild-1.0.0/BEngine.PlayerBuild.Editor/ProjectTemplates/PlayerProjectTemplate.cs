using System.Security.Cryptography;
using BEngine.Serialization;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Platform;
namespace BEngine.ProjectSystem.Editor;
public sealed class PlayerProjectTemplate : IProjectTemplate
{
    private static FileUtility FileSystem => EditorServiceAccess.GetRequiredService<FileUtility>();
    private static PathUtility Paths => EditorServiceAccess.GetRequiredService<PathUtility>();
    private static PlatformUtility Platform => EditorServiceAccess.GetRequiredService<PlatformUtility>();
    public void Initialize(ProjectWorkspace workspace) { EnsureAotContent(workspace, includeTemplateUi: true); CreateDefaultBuildSettings(workspace); }

    public static void EnsureRequiredAotInvariants(ProjectWorkspace workspace) =>
        EnsureAotContent(workspace, includeTemplateUi: false);

    private static void EnsureAotContent(ProjectWorkspace workspace, bool includeTemplateUi)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var root = workspace.ResolveInside(AotProjectLayout.AssetRoot);
        var uiRoot = workspace.ResolveInside(AotProjectLayout.UiAssetRoot);
        EnsureFolder(root);
        MigrateLegacyAotUiContent(workspace, root, uiRoot);
        if (includeTemplateUi) EnsureFolder(uiRoot);
        var assemblyDefinitionPath = workspace.ResolveInside(AotProjectLayout.AssemblyDefinitionAssetPath);
        WriteAssetIfMissing(assemblyDefinitionPath, """
            format: BEngine.AssemblyDefinition
            version: 1
            name: AOT
            rootNamespace: AOT
            references: [BEngine.UIElements]
            includePlatforms: []
            excludePlatforms: []
            defineConstraints: []
            # AOT is replaced before the hot-update domain is activated. Keep it startup-only
            # so fallback GameScripts cannot acquire an invalid dependency through auto refs.
            autoReferenced: false
            editorOnly: false
            allowUnsafeCode: false
            """, "DefaultImporter", "AssemblyDefinition");
        EnsureAotAssemblyIdentity(assemblyDefinitionPath, includeTemplateUi ||
            FileSystem.FileExists(workspace.ResolveInside(AotProjectLayout.UiDocumentAssetPath)));
        if (includeTemplateUi || FileSystem.FileExists(
                workspace.ResolveInside(AotProjectLayout.UiDocumentAssetPath)))
            WriteAssetIfMissing(workspace.ResolveInside(AotProjectLayout.SceneAssetPath), """
            format: BEngine.Scene
            version: 2
            id: a0700100-0000-4000-8000-000000000001
            name: AOT
            gameObjects:
            - id: a0700100-0000-4000-8000-000000000010
              name: AOT UI
              active: true
              tag: Untagged
              layer: 5
              isStatic: false
              transform:
                id: a0700100-0000-4000-8000-000000000011
                type: BEngine.Transform
                localPosition: { x: 0, y: 0 }
                localRotation: 0
                localScale: { x: 1, y: 1 }
                fields: {}
              components:
              - id: a0700100-0000-4000-8000-000000000012
                type: BEngine.UIElements.UIDocument
                enabled: true
                fields:
                  atlas: ''
                  interactable: true
                  referenceHeight: 720
                  referenceWidth: 1280
                  scaleMode: ScaleWithScreenSize
                  sortingLayer: 5
                  sortingOrder: 100
                  sourceAsset: Assets/AOT/UI/AOT.bxml
              - id: a0700100-0000-4000-8000-000000000013
                type: AOT.AotStartupView
                enabled: true
                fields: {}
            - id: a0700100-0000-4000-8000-000000000020
              name: AOT Camera
              active: true
              tag: MainCamera
              layer: 2
              isStatic: false
              transform:
                id: a0700100-0000-4000-8000-000000000021
                type: BEngine.Transform
                localPosition: { x: 0, y: 0 }
                localRotation: 0
                localScale: { x: 1, y: 1 }
                fields: {}
              components:
              - id: a0700100-0000-4000-8000-000000000022
                type: BEngine.Camera2D
                enabled: true
                fields:
                  backgroundColor: 0.063,0.086,0.102,1
                  clearMode: Color
                  cullingMask: 18446744073709551614
                  isMain: true
                  priority: 0
                  size: 5
                  viewportRect: 0,0,1,1
            """, "DefaultImporter", "Scene");
        else
            WriteAssetIfMissing(workspace.ResolveInside(AotProjectLayout.SceneAssetPath), """
                format: BEngine.Scene
                version: 2
                id: a0700100-0000-4000-8000-000000000001
                name: AOT
                gameObjects: []
                """, "DefaultImporter", "Scene");

        if (!includeTemplateUi) return;
        WriteAssetIfMissing(workspace.ResolveInside(AotProjectLayout.UiDocumentAssetPath), """
            <?xml version="1.0" encoding="utf-8"?>
            <UXML>
              <Style src="AOT.bss" />
              <VisualElement name="AotRoot" class="aot-root">
                <VisualElement class="aot-panel">
                  <Image name="Logo" src="Assets/AOT/UI/BEngine.png" class="aot-logo" />
                  <Label name="Status" text="Check for updates to continue" class="aot-status" />
                  <ProgressBar name="UpdateProgress" title="Waiting for update check" value="0" visible="false"
                               low-value="0" high-value="100" class="aot-progress" />
                  <Button name="CheckForUpdatesButton" text="Check for Updates"
                          tooltip="Check the content server for its active game version" class="aot-action" />
                  <Button name="EnterGameButton" text="Enter Game" visible="false"
                          tooltip="Enter the game after valid content is installed" class="aot-action" />
                </VisualElement>
                <VisualElement name="UpdateConfirmDialog" visible="false" class="aot-confirm-overlay">
                  <VisualElement class="aot-confirm-dialog">
                    <Label text="REMOTE VERSION AVAILABLE" class="aot-confirm-title" />
                    <Label name="UpdateConfirmMessage" text="A remote game version is available."
                           class="aot-confirm-message" />
                    <VisualElement class="aot-confirm-actions">
                      <Button name="CancelUpdateButton" text="Not Now"
                              tooltip="Continue with the installed game version" class="aot-confirm-button" />
                      <Button name="ConfirmUpdateButton" text="Switch" class="aot-confirm-button" />
                    </VisualElement>
                  </VisualElement>
                </VisualElement>
              </VisualElement>
            </UXML>
            """, "DefaultImporter", "UI Document");
        WriteAssetIfMissing(workspace.ResolveInside(AotProjectLayout.UiStyleAssetPath), """
            .aot-root {
                flex-grow: 1;
                align-items: center;
                justify-content: center;
                background-color: #10161AFF;
            }

            .aot-panel {
                width: 440px;
                padding: 24px;
                align-items: center;
            }

            .aot-logo {
                width: 256px;
                height: 128px;
                margin-bottom: 24px;
            }

            .aot-status {
                width: 392px;
                height: 52px;
                color: #F4F7F8FF;
            }

            .aot-progress {
                width: 392px;
                height: 26px;
                margin-top: 8px;
            }

            .aot-action {
                width: 220px;
                height: 36px;
                margin-top: 16px;
            }

            .aot-confirm-overlay {
                align-items: center;
                justify-content: center;
                background-color: #000000B8;
            }

            .aot-confirm-dialog {
                width: 380px;
                height: 210px;
                padding: 24px;
                align-items: center;
                background-color: #20282DFF;
                border-width: 1px;
                border-color: #5B6A72FF;
            }

            .aot-confirm-title {
                width: 332px;
                height: 28px;
                color: #FFFFFFFF;
                font-size: 18px;
            }

            .aot-confirm-message {
                width: 332px;
                height: 72px;
                margin-top: 12px;
                color: #D8E0E4FF;
            }

            .aot-confirm-actions {
                width: 332px;
                height: 40px;
                margin-top: 10px;
                flex-direction: row;
                justify-content: space-between;
            }

            .aot-confirm-button {
                width: 150px;
                height: 36px;
            }
            """, "DefaultImporter", "UI Style Sheet");
        WriteAssetIfMissing(workspace.ResolveInside(AotProjectLayout.UiControllerAssetPath), """
            using BEngine;
            using BEngine.Startup;
            using BEngine.UIElements;

            namespace AOT;

            public sealed class AotStartupView : MonoBehaviour
            {
                private IAotStartupFlow? _flow;
                private Label? _status;
                private ProgressBar? _progress;
                private Button? _check;
                private Button? _enter;
                private VisualElement? _confirmation;
                private Label? _confirmationMessage;
                private Button? _confirm;
                private Button? _cancel;

                public override void Start()
                {
                    var document = GetComponent<UIDocument>() ??
                                   throw new InvalidOperationException("AotStartupView requires a UIDocument.");
                    _status = Require<Label>(document, "Status");
                    _progress = Require<ProgressBar>(document, "UpdateProgress");
                    _check = Require<Button>(document, "CheckForUpdatesButton");
                    _enter = Require<Button>(document, "EnterGameButton");
                    _confirmation = Require<VisualElement>(document, "UpdateConfirmDialog");
                    _confirmationMessage = Require<Label>(document, "UpdateConfirmMessage");
                    _confirm = Require<Button>(document, "ConfirmUpdateButton");
                    _cancel = Require<Button>(document, "CancelUpdateButton");
                    _flow = gameObject.scene?.GetService<IAotStartupFlow>() ??
                            throw new InvalidOperationException("The Player did not provide an IAotStartupFlow service.");

                    _confirmation.style.position = Position.Absolute;
                    _confirmation.style.left = 0;
                    _confirmation.style.top = 0;
                    _confirmation.style.right = 0;
                    _confirmation.style.bottom = 0;
                    _check.clicked += OnCheckClicked;
                    _enter.clicked += OnEnterClicked;
                    _confirm.clicked += OnConfirmClicked;
                    _cancel.clicked += OnCancelClicked;
                    _flow.Changed += Apply;
                    Apply(_flow.Current);
                    Debug.Log("BENGINE_AOT_UI_READY|document=Assets/AOT/UI/AOT.bxml;" +
                              "status=Status;progress=UpdateProgress;check=CheckForUpdatesButton;" +
                              "dialog=UpdateConfirmDialog;confirm=ConfirmUpdateButton;" +
                              "cancel=CancelUpdateButton;enter=EnterGameButton");
                }

                public override void OnDestroy()
                {
                    if (_check is not null) _check.clicked -= OnCheckClicked;
                    if (_enter is not null) _enter.clicked -= OnEnterClicked;
                    if (_confirm is not null) _confirm.clicked -= OnConfirmClicked;
                    if (_cancel is not null) _cancel.clicked -= OnCancelClicked;
                    if (_flow is not null) _flow.Changed -= Apply;
                }

                private void OnCheckClicked()
                {
                    if (_flow?.Current.CanRetry == true) _flow.Retry();
                    else _flow?.CheckForUpdates();
                }

                private void OnEnterClicked() => _flow?.EnterGame();
                private void OnConfirmClicked() => _flow?.ConfirmUpdate();
                private void OnCancelClicked() => _flow?.DeclineUpdate();

                private void Apply(AotStartupSnapshot state)
                {
                    if (_status is null || _progress is null || _check is null || _enter is null ||
                        _confirmation is null || _confirmationMessage is null ||
                        _confirm is null || _cancel is null) return;
                    _status.text = string.IsNullOrWhiteSpace(state.Error)
                        ? state.Status
                        : state.Status + "\n" + SummarizeError(state.Error);
                    _status.tooltip = state.Error;
                    _progress.value = Math.Clamp(state.Progress * 100, 0, 100);
                    _progress.title = state.TotalBytes > 0
                        ? FormatBytes(state.CompletedBytes) + " / " + FormatBytes(state.TotalBytes)
                        : state.Status;
                    _progress.visible = state.Phase is AotStartupPhase.CheckingForUpdates or
                        AotStartupPhase.Downloading or AotStartupPhase.Verifying or AotStartupPhase.Activating or
                        AotStartupPhase.ValidatingInstalledContent;
                    _check.visible = state.CanCheckForUpdates || state.CanRetry;
                    _check.text = state.CanRetry ? "Retry" : "Check for Updates";
                    _enter.visible = state.CanEnterGame;
                    _confirmation.visible = state.RequiresUpdateConfirmation;
                    var targetVersion = string.IsNullOrWhiteSpace(state.TargetVersion)
                        ? "latest"
                        : state.TargetVersion;
                    _confirmationMessage.text = state.RequiresUpdateConfirmation
                        ? state.CanDeclineUpdate
                            ? $"Switch to remote version {targetVersion}?\n" +
                              $"Download {state.UpdateBundleCount} AB ({FormatBytes(state.TotalBytes)})."
                            : $"Remote version {targetVersion} is required because no playable content is installed.\n" +
                              $"Download {state.UpdateBundleCount} AB ({FormatBytes(state.TotalBytes)})."
                        : string.Empty;
                    _confirmationMessage.tooltip = state.RequiresUpdateConfirmation
                        ? $"Remote target version: {targetVersion}"
                        : string.Empty;
                    _confirm.SetEnabled(state.RequiresUpdateConfirmation);
                    _cancel.text = state.CanDeclineUpdate ? "Not Now" : "Version Required";
                    _cancel.tooltip = state.CanDeclineUpdate
                        ? "Continue with the installed game version"
                        : "A game version must be installed before continuing";
                    _cancel.SetEnabled(state.RequiresUpdateConfirmation && state.CanDeclineUpdate);
                }

                private static T Require<T>(UIDocument document, string name) where T : VisualElement =>
                    document.rootVisualElement.Q<T>(name) ??
                    throw new InvalidDataException(
                        "AOT.bxml has no " + name + " " + typeof(T).Name + ".");

                private static string FormatBytes(long bytes)
                {
                    if (bytes < 1024) return $"{bytes} B";
                    if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.0} KiB";
                    return $"{bytes / (1024d * 1024d):0.0} MiB";
                }

                private static string SummarizeError(string error) =>
                    error.Length <= 52 ? error : error[..49] + "...";
            }
            """, "ScriptImporter", "Script");

        var logo = workspace.ResolveInside(AotProjectLayout.LogoAssetPath);
        if (!FileSystem.FileExists(logo)) CopyFile(ResolveDefaultAotLogo(), logo);
        EnsureMeta(logo, "TextureImporter", "Texture", new Dictionary<string, string>
        {
            ["textureType"] = "Sprite",
            ["spritePivotX"] = "0.5",
            ["spritePivotY"] = "0.5"
        });
    }

    private static void MigrateLegacyAotUiContent(
        ProjectWorkspace workspace,
        string root,
        string uiRoot)
    {
        var fileNames = new[] { "AOT.bxml", "AOT.bss", "AotStartupView.cs", "BEngine.png" };
        if (fileNames.Any(fileName => FileSystem.FileExists(Paths.Combine(root, fileName))))
            EnsureFolder(uiRoot);

        foreach (var fileName in fileNames)
        {
            var source = Paths.Combine(root, fileName);
            var destination = Paths.Combine(uiRoot, fileName);
            if (!FileSystem.FileExists(source) || FileSystem.FileExists(destination)) continue;
            FileSystem.MoveFile(source, destination);
            if (FileSystem.FileExists(source + ".meta"))
                FileSystem.MoveFile(source + ".meta", destination + ".meta");
        }

        if (FileSystem.FileExists(workspace.ResolveInside(AotProjectLayout.UiDocumentAssetPath)))
        {
            ReplaceLegacyReference(workspace.ResolveInside(AotProjectLayout.SceneAssetPath),
                "Assets/Aot/AOT.bxml", AotProjectLayout.UiDocumentAssetPath);
            ReplaceLegacyReference(workspace.ResolveInside(AotProjectLayout.UiDocumentAssetPath),
                "Assets/Aot/BEngine.png", AotProjectLayout.LogoAssetPath);
        }
        if (FileSystem.FileExists(workspace.ResolveInside(AotProjectLayout.UiControllerAssetPath)))
            ReplaceLegacyReference(workspace.ResolveInside(AotProjectLayout.UiControllerAssetPath),
                "document=Assets/Aot/AOT.bxml", "document=" + AotProjectLayout.UiDocumentAssetPath);
    }

    private static void ReplaceLegacyReference(string path, string legacyValue, string currentValue)
    {
        if (!FileSystem.FileExists(path)) return;
        var contents = FileSystem.ReadAllText(path);
        if (!contents.Contains(legacyValue, StringComparison.Ordinal)) return;
        FileSystem.WriteAllText(path, contents.Replace(legacyValue, currentValue, StringComparison.Ordinal));
    }

    private static void CreateDefaultBuildSettings(ProjectWorkspace workspace)
    {
        BEngine.Editor.PlayerBuildSettingsStore.Save(workspace.RootPath,
            new BEngine.Editor.PlayerBuildSettings
            {
                TargetId = BEngine.Platform.BuildTargetUtility.Current.GetPlatformId(
                    BEngine.Platform.BuildTargetUtility.Current.InferCurrentDesktop(Platform).Platform),
                Scenes =
                [
                    new BEngine.Editor.PlayerBuildScene
                    {
                        Path = AotProjectLayout.SceneAssetPath
                    }
                ],
                HotResourceVersion = "v1",
                SplashImage = AotProjectLayout.LogoAssetPath,
                AndroidApplicationIdentifier =
                    BEngine.Editor.PlayerBuildSettingsStore.CreateDefaultApplicationIdentifier(workspace),
                IosBundleIdentifier =
                    BEngine.Editor.PlayerBuildSettingsStore.CreateDefaultApplicationIdentifier(workspace)
            });
    }

    private static void WriteAssetIfMissing(
        string path,
        string contents,
        string importer,
        string assetType)
    {
        if (!FileSystem.FileExists(path)) FileSystem.WriteAllText(path, contents);
        EnsureMeta(path, importer, assetType);
    }

    private static void EnsureFolder(string path)
    {
        FileSystem.CreateDirectory(path);
        if (FileSystem.FileExists(path + ".meta")) return;
        new AssetMetaDocument
        {
            Guid = Guid.NewGuid().ToString("N"),
            Importer = "FolderImporter",
            AssetType = "Folder"
        }.Save(path + ".meta");
    }

    private static void EnsureMeta(
        string path,
        string importer,
        string assetType,
        Dictionary<string, string>? settings = null)
    {
        if (!FileSystem.FileExists(path + ".meta")) WriteMeta(path, importer, assetType, settings);
    }

    private static void EnsureAotAssemblyIdentity(string path, bool usesTemplateUi)
    {
        var document = YamlUtility.Load<AssemblyDefinitionDocument>(path, FileSystem, Paths);
        if (!document.Format.Equals("BEngine.AssemblyDefinition", StringComparison.Ordinal) ||
            document.Version != 1)
            throw new InvalidDataException(
                $"The reserved AOT assembly definition has unsupported format '{document.Format}' v{document.Version}.");
        var previousName = document.Name ?? string.Empty;
        var changed = !previousName.Equals("AOT", StringComparison.Ordinal);
        document.Name = "AOT";
        document.References ??= [];
        changed |= document.References.RemoveAll(reference =>
            reference.Equals("AOT", StringComparison.OrdinalIgnoreCase) ||
            previousName.Length > 0 && reference.Equals(previousName, StringComparison.OrdinalIgnoreCase)) > 0;
        if (usesTemplateUi && !document.References.Contains("BEngine.UIElements", StringComparer.OrdinalIgnoreCase))
        {
            document.References.Add("BEngine.UIElements");
            changed = true;
        }
        EditorDataValidation.ValidateAssemblyDefinition(document);
        if (changed) FileSystem.WriteAllText(Paths.GetFullPath(path), YamlUtility.Serialize(document));
    }

    private static void WriteMeta(
        string path,
        string importer,
        string assetType,
        Dictionary<string, string>? settings = null)
    {
        using var stream = FileSystem.OpenRead(path);
        new AssetMetaDocument
        {
            Guid = Guid.NewGuid().ToString("N"),
            Importer = importer,
            AssetType = assetType,
            SourceHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
            Settings = settings ?? []
        }.Save(path + ".meta");
    }

    private static string ResolveDefaultAotLogo()
    {
        foreach (var start in new[] { Platform.BaseDirectory, Platform.CurrentDirectory })
        for (string? directory = Paths.GetFullPath(start); directory is not null;
             directory = ParentDirectory(directory))
        {
            foreach (var candidate in new[]
                     {
                         Paths.Combine(directory, "Editor", "Icons", "BEngine.png"),
                         Paths.Combine(directory, "src", "Core", "Editor", "Icons", "BEngine.png"),
                         Paths.Combine(directory, "Engine", "src", "Core", "Editor", "Icons", "BEngine.png")
                     })
                if (FileSystem.FileExists(candidate)) return candidate;
        }
        throw new FileNotFoundException(
            "The default BEngine AOT logo was not found. Re-export the engine before creating a project.");
    }

    private static string? ParentDirectory(string path)
    {
        var normalized = Paths.TrimEndingDirectorySeparator(Paths.GetFullPath(path));
        var parent = Paths.GetDirectoryName(normalized);
        return string.IsNullOrWhiteSpace(parent) ||
               parent.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            ? null
            : parent;
    }

    private static void CopyFile(string source, string destination)
    {
        using var input = FileSystem.OpenRead(source);
        using var output = FileSystem.OpenFile(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            64 * 1024, FileOptions.SequentialScan);
        input.CopyTo(output);
        FileSystem.Flush(output, flushToDisk: true);
    }
}
