using Avalonia;
using Icono.Core;
using Icono.Editor;
using Icono.Editor.Undo;
using Icono.Rendering;

namespace Icono.Mobile.Views;

/// <summary>The Region Color tool (the bucket) with the finger: a tap colours the shape under it (or the part of a shape that has parts); on a design
/// grid a finger that goes down and slides paints every square it passes - one undo step per stroke. With "Erase squares" on, the finger takes the
/// colour off. The logic is <see cref="RegionColoring"/>, shared with the Windows program.</summary>
public sealed partial class DocumentCanvas
{
    private Shape? _gridShape;
    private ShapeSnapshot? _gridBefore;
    private Dictionary<int, ArgbColor>? _gridWorking;
    private Point _gridLast;
    private bool _gridChanged;
    private bool _bucketTap;

    /// <summary>The finger went down with the bucket: on a design grid the stroke starts, elsewhere it is a tap (done when the finger lifts).</summary>
    private void BeginBucket(EditorContext context, Point at)
    {
        _bucketTap = false;
        if (RegionColoring.FindGridCell(context, at.X, at.Y) is not { } cell)
        {
            _bucketTap = true;
            return;
        }

        _gridShape = cell.Grid;
        _gridBefore = ShapeSnapshot.Capture(cell.Grid);
        _gridWorking = new Dictionary<int, ArgbColor>(cell.Grid.GridCells ?? new Dictionary<int, ArgbColor>());
        cell.Grid.GridCells = _gridWorking;
        _gridChanged = false;
        _gridLast = at;
        context.SelectedLayer = cell.Layer;
        context.SelectedShape = cell.Grid;
        PaintSquare(context, cell.Row, cell.Col);
    }

    private void PaintSquare(EditorContext context, int row, int col)
    {
        if (_gridWorking is null)
        {
            return;
        }

        var key = DesignGridMath.Key(row, col);
        if (context.GridPaintErase)
        {
            _gridChanged |= _gridWorking.Remove(key);
        }
        else if (!_gridWorking.TryGetValue(key, out var existing) || !existing.Equals(context.CurrentFillColor))
        {
            _gridWorking[key] = context.CurrentFillColor;
            _gridChanged = true;
        }

        context.RequestRedraw();
    }

    /// <summary>The finger slides on: every square on the way is painted (a finger can jump several squares between two moves).</summary>
    private void ContinueBucket(Point view)
    {
        if (_gridShape is null || _context is not { } context)
        {
            return;
        }

        var at = ToDocument(view);
        var cell = Math.Max(0.5, _gridShape.GridCellSize);
        var dx = at.X - _gridLast.X;
        var dy = at.Y - _gridLast.Y;
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / (cell * 0.5)));
        for (var i = 1; i <= steps; i++)
        {
            var t = i / (double)steps;
            if (DesignGridMath.TryGetCell(_gridShape, _gridLast.X + dx * t, _gridLast.Y + dy * t, out var row, out var col))
            {
                PaintSquare(context, row, col);
            }
        }

        _gridLast = at;
    }

    /// <summary>The finger lifted: a stroke on a grid is one undo step; a tap elsewhere colours the shape under it.</summary>
    private void FinishBucket()
    {
        if (_context is not { } context)
        {
            return;
        }

        if (_gridShape is { } grid && _gridBefore is { } before)
        {
            if (_gridChanged)
            {
                context.UndoManager.Push(UndoableAction.ForPropertyEdit([grid], [before], [ShapeSnapshot.Capture(grid)]));
            }
            else
            {
                grid.GridCells = before.GridCells; // nothing changed: no phantom edit is left
            }
        }
        else if (_bucketTap)
        {
            var at = ToDocument(_pressPoint);
            if (!RegionColoring.Apply(context, at.X, at.Y))
            {
                context.Notify("Region colour: tap on a shape (locked ones stay as they are).");
            }
        }

        _gridShape = null;
        _gridBefore = null;
        _gridWorking = null;
        _bucketTap = false;
        context.RequestRedraw();
    }

    /// <summary>A second finger came down: the stroke on a grid is taken back.</summary>
    private void CancelBucket()
    {
        if (_gridShape is { } grid && _gridBefore is { } before)
        {
            grid.GridCells = before.GridCells;
        }

        _gridShape = null;
        _gridBefore = null;
        _gridWorking = null;
        _bucketTap = false;
        _context?.RequestRedraw();
    }
}
