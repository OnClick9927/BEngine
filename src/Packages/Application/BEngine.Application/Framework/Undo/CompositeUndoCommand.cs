using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace BEngine.App.Undo;

/// <summary>One history entry containing several commands. A failure rolls back completed subcommands.
/// If compensation also fails, IsFaulted prevents replay until the caller restores application state.</summary>
public sealed class CompositeUndoCommand : IUndoCommand
{
    private readonly IUndoCommand[] _commands;
    private bool _executing;
    public CompositeUndoCommand(string name, IEnumerable<IUndoCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(commands);
        Name = name;
        var copy = new List<IUndoCommand>();
        foreach (var command in commands)
        {
            ArgumentNullException.ThrowIfNull(command);
            copy.Add(command);
        }
        _commands = copy.ToArray();
    }
    public string Name { get; }
    public int Count => _commands.Length;
    public bool IsFaulted { get; private set; }

    public void Execute()
    {
        CheckReplay();
        _executing = true;
        var executed = 0;
        try
        {
            for (; executed < _commands.Length; executed++) _commands[executed].Execute();
        }
        catch (Exception failure)
        {
            List<Exception>? compensationFailures = null;
            for (var index = executed - 1; index >= 0; index--)
            {
                try { _commands[index].Undo(); }
                catch (Exception error) { (compensationFailures ??= []).Add(error); }
            }
            Fail(failure, compensationFailures);
        }
        finally { _executing = false; }
    }
    public void Undo()
    {
        CheckReplay();
        _executing = true;
        var index = _commands.Length - 1;
        try
        {
            for (; index >= 0; index--) _commands[index].Undo();
        }
        catch (Exception failure)
        {
            List<Exception>? compensationFailures = null;
            // Reapply only operations that were successfully undone, in execution order.
            for (var restore = index + 1; restore < _commands.Length; restore++)
            {
                try { _commands[restore].Execute(); }
                catch (Exception error) { (compensationFailures ??= []).Add(error); }
            }
            Fail(failure, compensationFailures);
        }
        finally { _executing = false; }
    }
    private void Fail(Exception failure, List<Exception>? compensationFailures)
    {
        if (compensationFailures is null)
        {
            if (failure is UndoCompensationException) IsFaulted = true;
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        IsFaulted = true;
        compensationFailures.Insert(0, failure);
        throw new UndoCompensationException("A composite operation and its compensation both failed; restore application state before clearing history.", compensationFailures);
    }
    private void CheckReplay()
    {
        if (_executing) throw new InvalidOperationException("A composite command cannot execute recursively.");
        if (IsFaulted) throw new InvalidOperationException("The composite command has failed compensation and cannot be replayed.");
    }
}

/// <summary>The application state may be partially changed; normal history replay must stop.</summary>
public sealed class UndoCompensationException(string message, IEnumerable<Exception> failures)
    : AggregateException(message, failures);
