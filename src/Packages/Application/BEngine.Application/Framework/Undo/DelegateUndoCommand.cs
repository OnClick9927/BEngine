using System;

namespace BEngine.App.Undo;

/// <summary>Captures before/after operations; delegates are retained only while this command is in history.</summary>
public sealed class DelegateUndoCommand : IUndoCommand
{
    private readonly Action _execute;
    private readonly Action _undo;
    public DelegateUndoCommand(string name, Action execute, Action undo)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(undo);
        Name = name;
        _execute = execute;
        _undo = undo;
    }
    public string Name { get; }
    public void Execute() => _execute();
    public void Undo() => _undo();
}
