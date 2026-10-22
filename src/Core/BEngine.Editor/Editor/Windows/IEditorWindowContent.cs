namespace BEngine.Editor;

public interface IEditorWindowContent : IEditorGraphicsViewport
{
    void SynchronizeLayout(Fix64 width, Fix64 height);
    void HandleEvent(Event current);
    void ReleaseGraphicsDevices();
}
