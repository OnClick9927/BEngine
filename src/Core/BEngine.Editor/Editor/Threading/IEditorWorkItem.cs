namespace BEngine.Editor;

internal interface IEditorWorkItem
{
    string Name { get; }
    EditorTaskPriority Priority { get; }
    Task Completion { get; }

    ValueTask ExecuteAsync(CancellationToken shutdownToken);
    void Cancel(CancellationToken cancellationToken);
}
