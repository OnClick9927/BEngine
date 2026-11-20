using System.Collections.Generic;

namespace BEngine.App.Undo;

/// <summary>A synchronous reversible operation. Each direction must either succeed, or leave its own state unchanged.</summary>
public interface IUndoCommand
{
    string Name { get; }
    void Execute();
    void Undo();
}

public interface IUndoService : IGameService
{
    int Capacity { get; set; }
    int UndoCount { get; }
    int RedoCount { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }
    bool IsFaulted { get; }
    void Execute(IUndoCommand command);
    void RecordExecuted(IUndoCommand command);
    void ExecuteBatch(string name, IEnumerable<IUndoCommand> commands);
    bool Undo();
    bool Redo();
    void Clear();
}
