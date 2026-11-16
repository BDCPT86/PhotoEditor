using FrameForge.Core.Editing;

namespace FrameForge.Application.Editing;

/// <summary>
/// Undo/redo of one photo's recipe within a Develop session. Each entry is a whole recipe, so undo is exact.
/// A slider drag records one entry (on release), not one per pixel of movement. Bounded.
/// </summary>
public sealed class EditHistory(int capacity = 200)
{
    private readonly LinkedList<EditRecipe> _undo = new();
    private readonly Stack<EditRecipe> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Records the state before a change. Clears redo.</summary>
    public void Record(EditRecipe before)
    {
        ArgumentNullException.ThrowIfNull(before);
        _undo.AddLast(before);
        if (_undo.Count > capacity)
        {
            _undo.RemoveFirst();
        }

        _redo.Clear();
    }

    /// <returns>The recipe to restore, or null if there is nothing to undo.</returns>
    public EditRecipe? Undo(EditRecipe current)
    {
        if (_undo.Last is not { } last)
        {
            return null;
        }

        _undo.RemoveLast();
        _redo.Push(current);
        return last.Value;
    }

    public EditRecipe? Redo(EditRecipe current)
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        _undo.AddLast(current);
        return _redo.Pop();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
