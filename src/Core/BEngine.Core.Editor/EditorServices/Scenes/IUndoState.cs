namespace BEngine.Editor;

internal interface IUndoState
{
    long EstimatedBytes => 128;
    IUndoState CaptureInverse();
    void Restore();
}
