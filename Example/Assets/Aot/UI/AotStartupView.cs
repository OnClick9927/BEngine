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
        Debug.Log("BENGINE_AOT_UI_READY|document=Assets/Aot/UI/AOT.uxml;" +
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
            _confirmation is null || _confirmationMessage is null || _confirm is null || _cancel is null) return;
        _status.text = string.IsNullOrWhiteSpace(state.Error)
            ? state.Status
            : $"{state.Status}\n{SummarizeError(state.Error)}";
        _status.tooltip = state.Error;
        _progress.value = Math.Clamp(state.Progress * 100, 0, 100);
        _progress.title = state.TotalBytes > 0
            ? $"{FormatBytes(state.CompletedBytes)} / {FormatBytes(state.TotalBytes)}"
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
        throw new InvalidDataException($"AOT.uxml has no {name} {typeof(T).Name}.");

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.0} KiB";
        return $"{bytes / (1024d * 1024d):0.0} MiB";
    }

    private static string SummarizeError(string error) =>
        error.Length <= 52 ? error : error[..49] + "...";
}
