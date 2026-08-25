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
            EditorUtility.progressChanged += states.Add;
            try
            {
                EditorUtility.DisplayProgressBar("Import", "Scanning", -2);
                Assert(EditorUtility.progressInfo is
                    { IsVisible: true, IsCancelable: false, Progress: 0 }, "Progress minimum was not clamped.");

                var canceled = EditorUtility.DisplayCancelableProgressBar("Bake", "Building", 2);
                Assert(!canceled, "A fresh cancelable task must not start canceled.");
                Assert(EditorUtility.progressInfo is
                    { IsVisible: true, IsCancelable: true, Progress: 1 }, "Cancelable progress state is invalid.");

                typeof(EditorUtility).GetMethod("RequestProgressCancellation",
                    BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
                Assert(EditorUtility.DisplayCancelableProgressBar("Bake", "Finishing", 0.75f),
                    "Cancelable progress did not preserve the cancellation request.");

                EditorUtility.ClearProgressBar();
                Assert(!EditorUtility.progressInfo.IsVisible, "ClearProgressBar did not clear the active task.");
                Assert(states.Count == 5 && !states[^1].IsVisible, "Progress events did not describe the full lifecycle.");
                VerifyGpuStartupProgressContract();
            }
            finally
            {
                EditorUtility.progressChanged -= states.Add;
                EditorUtility.ClearProgressBar();
            }

            Console.WriteLine("EDITOR_PROGRESS_OK|visible,clamp,cancel-request,clear,event,imgui-startup-window");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"EDITOR_PROGRESS_FAILED|{exception}");
            return 1;
        }
    }

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
}
