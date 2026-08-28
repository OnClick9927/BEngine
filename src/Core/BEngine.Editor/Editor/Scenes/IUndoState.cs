namespace BEngine.Editor;

internal interface IUndoState
{
    IUndoState CaptureInverse();
    void Restore();
}
