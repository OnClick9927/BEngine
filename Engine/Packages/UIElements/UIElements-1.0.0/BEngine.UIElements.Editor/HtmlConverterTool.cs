using BEngine.Editor;

namespace BEngine.UIElements.Editor;

[EditorTool("html-converter")]
public sealed class HtmlConverterTool : IEditorTool
{
    public void Open(IEditorView? target) => HtmlConverterWindow.Open();
}
