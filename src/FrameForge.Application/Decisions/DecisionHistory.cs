using FrameForge.Core.Decisions;

namespace FrameForge.Application.Decisions;

/// <summary>One photo's decision before and after a change.</summary>
public sealed record DecisionDelta(Guid PhotoId, PhotoDecision Before, PhotoDecision After);

/// <summary>A user action that changed one or more photos; undone and redone as a unit.</summary>
public sealed record DecisionChange(string Description, IReadOnlyList<DecisionDelta> Deltas)
{
    public IReadOnlyList<(Guid PhotoId, PhotoDecision Decision)> AfterValues =>
        Deltas.Select(d => (d.PhotoId, d.After)).ToList();

    public IReadOnlyList<(Guid PhotoId, PhotoDecision Decision)> BeforeValues =>
        Deltas.Select(d => (d.PhotoId, d.Before)).ToList();
}

/// <summary>
/// Bounded undo/redo stack of decision changes. Not thread-safe: owned by the UI thread.
/// Recording a new change clears the redo stack, as in most editors.
/// </summary>
public sealed class DecisionHistory
{
    private readonly LinkedList<DecisionChange> _undo = new();
    private readonly Stack<DecisionChange> _redo = new();

    public DecisionHistory(int capacity = 200)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    public int Capacity { get; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    public void Record(DecisionChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        _undo.AddLast(change);
        if (_undo.Count > Capacity)
        {
            _undo.RemoveFirst();
        }

        _redo.Clear();
    }

    /// <summary>Returns the most recent change (apply its <see cref="DecisionDelta.Before"/> values), or null.</summary>
    public DecisionChange? Undo()
    {
        if (_undo.Last is not { } last)
        {
            return null;
        }

        _undo.RemoveLast();
        _redo.Push(last.Value);
        return last.Value;
    }

    /// <summary>Returns the most recently undone change (apply its <see cref="DecisionDelta.After"/> values), or null.</summary>
    public DecisionChange? Redo()
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        var change = _redo.Pop();
        _undo.AddLast(change);
        return change;
    }

    /// <summary>Removes a change that could not be persisted, so undo never refers to an unsaved state.</summary>
    public void Discard(DecisionChange change)
    {
        _undo.Remove(change);
        if (_redo.Count > 0 && ReferenceEquals(_redo.Peek(), change))
        {
            _redo.Pop();
        }
    }
}
