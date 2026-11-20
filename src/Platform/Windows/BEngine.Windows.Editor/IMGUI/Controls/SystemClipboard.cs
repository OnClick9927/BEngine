using System.Runtime.InteropServices;

namespace BEngine.Editor;

internal sealed class SystemClipboard : IClipboardService
{
    private const int Attempts = 5;

    public bool TryGetText(out string text)
    {
        text = string.Empty;
        if (!OperatingSystem.IsWindows()) return false;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            try
            {
                text = System.Windows.Forms.Clipboard.ContainsText(
                    System.Windows.Forms.TextDataFormat.UnicodeText)
                    ? System.Windows.Forms.Clipboard.GetText(System.Windows.Forms.TextDataFormat.UnicodeText)
                    : string.Empty;
                return true;
            }
            catch (ExternalException)
            {
                if (attempt + 1 >= Attempts) return false;
                Thread.Sleep(1 << attempt);
            }
            catch (ThreadStateException)
            {
                return false;
            }
        }

        return false;
    }

    public bool TrySetText(string text)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            System.Windows.Forms.Clipboard.SetDataObject(text, copy: true,
                retryTimes: Attempts, retryDelay: 5);
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
        catch (ThreadStateException)
        {
            return false;
        }
    }
}
