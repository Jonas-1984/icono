using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Icono.App.State.Undo;

/// <summary>Owns the undo/redo stacks for the active document. One instance per <see cref="EditorContext"/>.</summary>
public sealed class UndoManager : INotifyPropertyChanged
{
    private const int MaxEntries = 200;

    private readonly Stack<IUndoableAction> _undoStack = new();
    private readonly Stack<IUndoableAction> _redoStack = new();
    private bool _isApplying;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>Records a completed gesture. Ignored if called while Undo/Redo is itself applying
    /// an action, so replaying a snapshot doesn't re-enter the undo stack.</summary>
    public void Push(IUndoableAction action)
    {
        if (_isApplying)
        {
            return;
        }

        _undoStack.Push(action);
        _redoStack.Clear();

        while (_undoStack.Count > MaxEntries)
        {
            // Stack<T> has no RemoveLast; the cap is generous enough that trimming isn't hot-path,
            // so rebuild from the newest MaxEntries entries.
            var kept = _undoStack.Take(MaxEntries).Reverse().ToArray();
            _undoStack.Clear();
            foreach (var kept0 in kept)
            {
                _undoStack.Push(kept0);
            }

            break;
        }

        RaiseChanged();
    }

    public void Undo()
    {
        if (!_undoStack.TryPop(out var action))
        {
            return;
        }

        _isApplying = true;
        try
        {
            action.Undo();
        }
        finally
        {
            _isApplying = false;
        }

        _redoStack.Push(action);
        RaiseChanged();
    }

    public void Redo()
    {
        if (!_redoStack.TryPop(out var action))
        {
            return;
        }

        _isApplying = true;
        try
        {
            action.Redo();
        }
        finally
        {
            _isApplying = false;
        }

        _undoStack.Push(action);
        RaiseChanged();
    }

    private void RaiseChanged([CallerMemberName] string? _ = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanUndo)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRedo)));
    }
}
