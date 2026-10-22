namespace BEngine.Editor;
public interface IClipboardService
{
    bool TryGetText(out string text);
    bool TrySetText(string text);
}
