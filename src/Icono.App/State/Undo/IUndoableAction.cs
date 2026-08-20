namespace Icono.App.State.Undo;

/// <summary>One user gesture that can be reverted and reapplied.</summary>
public interface IUndoableAction
{
    void Undo();

    void Redo();
}
