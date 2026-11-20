using System;
using System.Collections.Generic;

namespace BEngine.App.Undo;

/// <summary>A bounded, single-threaded undo journal owned by one GameDomain.
/// Commands are borrowed: discarding history releases references without disposing user commands.</summary>
public sealed class UndoService : GameService, IUndoService
{
    private readonly List<IUndoCommand> _history = [];
    private int _position;
    private int _capacity;
    private bool _busy;
    private bool _disposed;
    public UndoService(int capacity = 128)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _capacity = capacity;
    }
    public int Capacity
    {
        get => _capacity;
        set
        {
            CheckMutable(allowFaulted: true);
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _capacity = value;
            Trim();
        }
    }
    public int UndoCount => _position;
    public int RedoCount => _history.Count - _position;
    public bool CanUndo => !_disposed && !IsFaulted && _position > 0;
    public bool CanRedo => !_disposed && !IsFaulted && _position < _history.Count;
    public bool IsFaulted { get; private set; }

    public void Execute(IUndoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        CheckMutable();
        _busy = true;
        try
        {
            try { command.Execute(); }
            catch (UndoCompensationException) { IsFaulted = true; throw; }
            if (!_disposed) Commit(command);
        }
        finally { _busy = false; }
    }
    /// <summary>Records an operation already applied by the caller, without executing it again.</summary>
    public void RecordExecuted(IUndoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        CheckMutable();
        Commit(command);
    }
    public void ExecuteBatch(string name, IEnumerable<IUndoCommand> commands)
    {
        CheckMutable();
        // Enumeration is also guarded: an input iterator must not change the same history mid-batch.
        _busy = true;
        CompositeUndoCommand composite;
        try { composite = new CompositeUndoCommand(name, commands); }
        finally { _busy = false; }
        if (composite.Count > 0) Execute(composite);
    }
    public bool Undo()
    {
        CheckMutable();
        if (_position == 0) return false;
        _busy = true;
        try
        {
            try { _history[_position - 1].Undo(); }
            catch (UndoCompensationException) { IsFaulted = true; throw; }
            if (!_disposed) _position--;
            return true;
        }
        finally { _busy = false; }
    }
    public bool Redo()
    {
        CheckMutable();
        if (_position == _history.Count) return false;
        _busy = true;
        try
        {
            try { _history[_position].Execute(); }
            catch (UndoCompensationException) { IsFaulted = true; throw; }
            if (!_disposed) _position++;
            return true;
        }
        finally { _busy = false; }
    }
    /// <summary>Forgets history and clears a compensation fault. It does not undo applied changes.
    /// After failed compensation the caller must first restore its own application state.</summary>
    public void Clear()
    {
        CheckMutable(allowFaulted: true);
        _history.Clear();
        _position = 0;
        IsFaulted = false;
    }
    /// <summary>Copies names into a caller-owned buffer, avoiding a new list for history UI refreshes.</summary>
    public void CopyHistoryNames(ICollection<string> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckMutable(allowFaulted: true);
        _busy = true;
        try { foreach (var command in _history) destination.Add(command.Name); }
        finally { _busy = false; }
    }
    protected override void OnDispose()
    {
        _disposed = true;
        _history.Clear();
        _position = 0;
    }
    private void Commit(IUndoCommand command)
    {
        if (_position < _history.Count) _history.RemoveRange(_position, _history.Count - _position);
        if (_capacity == 0) return;
        _history.Add(command);
        _position++;
        Trim();
    }
    private void Trim()
    {
        var excess = _history.Count - _capacity;
        if (excess <= 0) return;
        // Drop farthest redo first; never keep a redo command while dropping one of its prerequisites.
        var redo = Math.Min(excess, _history.Count - _position);
        if (redo > 0) _history.RemoveRange(_history.Count - redo, redo);
        excess -= redo;
        if (excess > 0)
        {
            _history.RemoveRange(0, excess);
            _position -= excess;
        }
    }
    private void CheckMutable(bool allowFaulted = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) throw new InvalidOperationException("Undo history cannot be changed from a command callback or batch iterator.");
        if (!allowFaulted && IsFaulted) throw new InvalidOperationException("Undo compensation failed. Restore application state and Clear before continuing.");
    }
}
