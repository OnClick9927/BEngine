namespace BEngine.Editor;

public sealed class SystemEditorHostOutput : IEditorHostOutput
{
    public void Beep() => Console.Write('\a');
}
