using System.Reflection;
using BEngine.Editor;

namespace BEngine.ExampleTests.EditorProgress;

internal static class Program
{
    private static int Main()
    {
        try
        {
            var states = new List<EditorProgressInfo>();
            var previousPlatform = EditorUtility.Platform;
            var progressPlatform = new CapturingEditorUtilityPlatform();
            EditorUtility.Platform = progressPlatform;
            EditorUtility.progressChanged += states.Add;
            try
            {
                EditorUtility.DisplayProgressBar("Import", "Scanning", -2);
                Assert(EditorUtility.progressInfo is
                    { IsVisible: true, IsCancelable: false, Progress: 0 }, "Progress minimum was not clamped.");
                Assert(progressPlatform.ProgressUpdates is
                    [{ IsVisible: true, IsCancelable: false, Progress: 0 }] &&
                    progressPlatform.CancellationCallback is null && progressPlatform.IsProgressVisible,
                    "DisplayProgressBar did not immediately present its normalized platform state.");

                var canceled = EditorUtility.DisplayCancelableProgressBar("Bake", "Building", 2);
                Assert(!canceled, "A fresh cancelable task must not start canceled.");
                Assert(EditorUtility.progressInfo is
                    { IsVisible: true, IsCancelable: true, Progress: 1 }, "Cancelable progress state is invalid.");
                Assert(progressPlatform.ProgressUpdates.Count == 2 &&
                       progressPlatform.CancellationCallback is not null,
                    "Cancelable progress did not provide a platform cancellation callback.");

                progressPlatform.RequestCancellation();
                Assert(EditorUtility.progressInfo.IsCancellationRequested,
                    "The platform cancellation callback did not set the editor Cancel flag.");
                Assert(EditorUtility.DisplayCancelableProgressBar("Bake", "Finishing", 0.75f),
                    "Cancelable progress did not preserve the cancellation request.");
                Assert(progressPlatform.ProgressUpdates is
                    [_, _, { Info: "Finishing", Progress: 0.75f, IsCancellationRequested: true }],
                    "Repeated progress updates did not reuse the platform lifecycle or preserve Cancel.");

                EditorUtility.ClearProgressBar();
                Assert(!EditorUtility.progressInfo.IsVisible, "ClearProgressBar did not clear the active task.");
                Assert(states.Count == 5 && !states[^1].IsVisible, "Progress events did not describe the full lifecycle.");
                Assert(progressPlatform is { IsProgressVisible: false, ClearProgressCalls: 1 },
                    "ClearProgressBar did not close the platform progress presentation.");
                EditorUtility.ClearProgressBar();
                Assert(progressPlatform.ClearProgressCalls == 2,
                    "Repeated ClearProgressBar was not safely forwarded to the platform.");
                VerifyEditorUtilityDialogsAndPanels();
                VerifyPopupMenuUsesMenuRegistry();
                VerifyGpuStartupProgressContract();
            }
            finally
            {
                EditorUtility.progressChanged -= states.Add;
                EditorUtility.ClearProgressBar();
                EditorUtility.Platform = previousPlatform;
            }

            Console.WriteLine("EDITOR_PROGRESS_OK|visible,clamp,cancel-request,clear,event,dialogs,file-panels," +
                              "default-app,popup-menu,platform-lifecycle,imgui-startup-window");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"EDITOR_PROGRESS_FAILED|{exception}");
            return 1;
        }
    }

    private static void VerifyEditorUtilityDialogsAndPanels()
    {
        var previous = EditorUtility.Platform;
        var platform = new CapturingEditorUtilityPlatform
        {
            DialogResult = 1,
            FileResult = @"C:\Assets\Character.png",
            FolderResult = @"C:\Assets"
        };
        EditorUtility.Platform = platform;
        try
        {
            Assert(!EditorUtility.DisplayDialog(" Confirm ", "Continue?", "Yes", "No"),
                "DisplayDialog did not map the cancel button to false.");
            Assert(platform.Title == "Confirm" && platform.Buttons.SequenceEqual(["Yes", "No"]),
                "DisplayDialog did not preserve its title and custom button labels.");

            platform.DialogResult = 2;
            Assert(EditorUtility.DisplayDialogComplex("Save", "Choose", "Save", "Cancel", "Discard") == 2,
                "DisplayDialogComplex did not return the selected custom button index.");

            Assert(EditorUtility.OpenFilePanel("Texture", @"C:\Assets", "png") == platform.FileResult &&
                   platform.Filter == "PNG files (*.png)|*.png|All files (*.*)|*.*",
                "OpenFilePanel did not build the expected Windows file filter.");
            EditorUtility.OpenFilePanelWithFilters("Image", @"C:\Assets",
                ["Images", "png,jpg", "All files", "*"]);
            Assert(platform.Filter == "Images (*.png;*.jpg)|*.png;*.jpg|All files (*.*)|*.*",
                "OpenFilePanelWithFilters did not preserve alternating names and extension groups.");
            Assert(EditorUtility.OpenFolderPanel("Folder", @"C:\", "Assets") == platform.FolderResult &&
                   platform.DefaultName == "Assets", "OpenFolderPanel did not forward its initial folder.");

            EditorUtility.OpenWithDefaultApp("https://example.invalid/help");
            Assert(platform.OpenTarget == "https://example.invalid/help",
                "OpenWithDefaultApp did not forward the target to the platform shell.");

            try
            {
                EditorUtility.OpenFilePanelWithFilters("Broken", string.Empty, ["Images"]);
                throw new InvalidOperationException("An odd file-filter array was accepted.");
            }
            catch (ArgumentException)
            {
            }
        }
        finally
        {
            EditorUtility.Platform = previous;
        }
    }

    private static void VerifyPopupMenuUsesMenuRegistry()
    {
        IReadOnlyList<GenericMenuItem>? presented = null;
        var previous = GenericMenuDispatcher.Handler;
        GenericMenuDispatcher.Handler = items => presented = items.ToArray();
        var context = new GameObject("Popup context");
        try
        {
            _popupContext = null;
            EditorUtility.DisplayPopupMenu(new Rect(5, 8, 120, 20), "Tests/EditorUtility",
                new MenuCommand(context));
            var item = presented?.SingleOrDefault(candidate => candidate.Path == "Execute");
            Assert(item is { Enabled: true, Action: not null },
                "DisplayPopupMenu did not expose externally attributed menu commands.");
            item!.Action!();
            Assert(ReferenceEquals(_popupContext, context),
                "DisplayPopupMenu did not preserve the MenuCommand context.");
        }
        finally
        {
            GenericMenuDispatcher.Handler = previous;
            BObject.DestroyImmediate(context);
        }
    }

    private static BObject? _popupContext;

    [MenuItem("Tests/EditorUtility/Execute")]
    private static void ExecutePopup(MenuCommand command) => _popupContext = command.context;

    private static void VerifyGpuStartupProgressContract()
    {
        var assembly = typeof(EditorUtility).Assembly;
        var startupWindow = assembly.GetType("BEngine.Editor.GpuStartupProgressWindow") ??
                            throw new InvalidOperationException("GPU startup progress window was not found.");
        Assert(typeof(IDisposable).IsAssignableFrom(startupWindow),
            "GPU startup progress window must have deterministic lifetime management.");
        Assert(startupWindow.GetMethod("Show", BindingFlags.Static | BindingFlags.Public) is not null,
            "GPU startup progress window does not expose its startup entry point.");
        Assert(startupWindow.GetMethod("Complete", BindingFlags.Instance | BindingFlags.Public) is not null,
            "GPU startup progress window does not expose first-frame completion.");

        var nativeWindow = assembly.GetType("BEngine.Editor.ImGuiNativeWindow") ??
                           throw new InvalidOperationException("IMGUI native window was not found.");
        Assert(typeof(IDisposable).IsAssignableFrom(nativeWindow),
            "IMGUI native window must have deterministic lifetime management.");
        Assert(nativeWindow.GetEvent("firstFrameRendered", BindingFlags.Instance | BindingFlags.Public) is not null,
            "IMGUI native window does not expose first-frame completion.");
        Assert(startupWindow.GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)?.FieldType ==
               nativeWindow, "Startup progress is not hosted by the current IMGUI native window.");

        var editorApplication = assembly.GetType("BEngine.Editor.GpuEditorApplication") ??
                                throw new InvalidOperationException("GPU editor application was not found.");
        Assert(editorApplication.GetMethod("Run", BindingFlags.Instance | BindingFlags.Public,
                   binder: null, types: [typeof(Action)], modifiers: null) is not null,
            "Editor Run must keep the loading window alive until the first rendered frame.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CapturingEditorUtilityPlatform : IEditorUtilityPlatform
    {
        internal int DialogResult { get; set; }
        internal string FileResult { get; init; } = string.Empty;
        internal string FolderResult { get; init; } = string.Empty;
        internal string Title { get; private set; } = string.Empty;
        internal string Filter { get; private set; } = string.Empty;
        internal string DefaultName { get; private set; } = string.Empty;
        internal string OpenTarget { get; private set; } = string.Empty;
        internal IReadOnlyList<string> Buttons { get; private set; } = [];
        internal List<EditorProgressInfo> ProgressUpdates { get; } = [];
        internal Action? CancellationCallback { get; private set; }
        internal bool IsProgressVisible { get; private set; }
        internal int ClearProgressCalls { get; private set; }

        public int DisplayDialog(string title, string message, IReadOnlyList<string> buttons)
        {
            Title = title;
            Buttons = buttons.ToArray();
            return DialogResult;
        }

        public string OpenFilePanel(string title, string directory, string filter)
        {
            Title = title;
            Filter = filter;
            return FileResult;
        }

        public string OpenFolderPanel(string title, string folder, string defaultName)
        {
            Title = title;
            DefaultName = defaultName;
            return FolderResult;
        }

        public void OpenWithDefaultApp(string target) => OpenTarget = target;

        public void ShowProgress(EditorProgressInfo progress, Action? requestCancellation)
        {
            ProgressUpdates.Add(progress);
            CancellationCallback = requestCancellation;
            IsProgressVisible = true;
        }

        public void ClearProgress()
        {
            ClearProgressCalls++;
            CancellationCallback = null;
            IsProgressVisible = false;
        }

        internal void RequestCancellation() => CancellationCallback?.Invoke();
    }
}
