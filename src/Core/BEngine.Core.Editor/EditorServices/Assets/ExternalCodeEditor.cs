using EditorCapabilities = BEngine.Editor.EditorServices;
using System.ComponentModel;
using BEngine.Platform;

namespace BEngine.Editor;

internal static class ExternalCodeEditor
{
    public static bool Open(string filePath, int lineNumber, int columnNumber)
    {
        if (CodeEditorIntegration.TryOpen(new CodeEditorOpenRequest(
                EditorApplication.projectPath, filePath, lineNumber, columnNumber))) return true;
        try
        {
            EditorCapabilities.GetRequiredService<ProcessHostUtility>().StartDetached(new ProcessHostRequest(filePath)
            { UseShellExecute = true, CreateNoWindow = false });
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException) { return false; }
    }
}
