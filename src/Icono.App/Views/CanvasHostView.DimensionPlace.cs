using System.Windows;
using System.Windows.Input;
using Icono.App.State;
using Icono.App.State.Undo;
using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.App.Views;

/// <summary>Placing the dimension of a wall, a door or a window with the mouse alone, like the "dimension line location" point of AutoCAD: press
/// on the number of the dimension and drag - its line follows the pointer, on either side of the wall, at any distance - and let go where it
/// should stay. A click without dragging still opens the length editor (type the length you want).</summary>
public partial class CanvasHostView
{
    /// <summary>How far (screen px) the pointer has to travel before a press on a number is a drag and not a click.</summary>
    private const double DimensionDragPx = 4;

    private Shape? _dimensionShape;
    private int _dimensionSegment;
    private SymbolDimension? _dimensionOfSymbol;
    private ShapeSnapshot? _dimensionBefore;
    private Point _dimensionPressPoint;
    private Point _dimensionPressScreen;
    private bool _dimensionDragging;

    /// <summary>A press on the number of a wall or door/window dimension: remembers it. What happens (length editor or placing the line) is
    /// decided when the mouse moves or is let go. True when the point is on such a number.</summary>
    private bool TryPressDimensionNumber(Point point, Point screen)
    {
        if (FindWallDimensionNumber(point) is { } wall)
        {
            _dimensionShape = wall.Wall;
            _dimensionSegment = wall.Segment;
            _dimensionOfSymbol = null;
        }
        else if (FindSymbolDimensionNumber(point) is { } symbol)
        {
            _dimensionShape = symbol.Symbol;
            _dimensionOfSymbol = symbol.Dimension;
        }
        else
        {
            return false;
        }

        _dimensionBefore = ShapeSnapshot.Capture(_dimensionShape);
        _dimensionPressPoint = point;
        _dimensionPressScreen = screen;
        _dimensionDragging = false;
        CaptureMouse();
        return true;
    }

    /// <summary>The pointer moved while the number is pressed: past a few pixels the dimension line is placed where the pointer is.</summary>
    private void ContinueDimensionPress(MouseEventArgs e)
    {
        if (_context is null || _dimensionShape is not { } shape)
        {
            return;
        }

        if (!_dimensionDragging)
        {
            var screen = e.GetPosition(this);
            if (Math.Abs(screen.X - _dimensionPressScreen.X) < DimensionDragPx && Math.Abs(screen.Y - _dimensionPressScreen.Y) < DimensionDragPx)
            {
                return;
            }

            _dimensionDragging = true;
            _context.SelectedShape = shape;
        }

        var at = ToDocumentSpace(e.GetPosition(SkiaCanvas));
        var point = new SKPoint((float)at.X, (float)at.Y);
        if (shape.Kind == ShapeKind.Wall)
        {
            if (WallRenderer.DimensionLineThrough(shape, _dimensionSegment, point) is { } line)
            {
                SetWallDimensionLine(shape, _dimensionSegment, line.Offset, line.Flip);
            }
        }
        else
        {
            SymbolMeasurements.PlaceDimensionLine(_context.Document, shape, point);
        }

        _context.RequestRedraw();
    }

    /// <summary>The number is let go: after a drag the line stays where it is (one undo step); after a click the length editor opens.</summary>
    private void EndDimensionPress()
    {
        var shape = _dimensionShape;
        var symbol = _dimensionOfSymbol;
        var segment = _dimensionSegment;
        var before = _dimensionBefore;
        var dragged = _dimensionDragging;
        ResetDimensionPress();

        if (_context is null || shape is null || before is not { })
        {
            return;
        }

        if (!dragged)
        {
            if (shape.Kind == ShapeKind.Wall)
            {
                BeginWallLengthEdit(shape, segment);
            }
            else if (symbol is not null)
            {
                BeginSymbolDimensionEdit(shape, symbol);
            }

            return;
        }

        var after = ShapeSnapshot.Capture(shape);
        if (!after.Equals(before.Value))
        {
            _context.UndoManager.Push(UndoableAction.ForPropertyEdit([shape], [before.Value], [after]));
        }

        _context.RequestRedraw();
    }

    private void ResetDimensionPress()
    {
        _dimensionShape = null;
        _dimensionOfSymbol = null;
        _dimensionBefore = null;
        _dimensionDragging = false;
        ReleaseMouseCapture();
    }

    /// <summary>Esc while a dimension line is being dragged: it jumps back to where it was. True when there was a drag.</summary>
    public bool CancelDimensionPress()
    {
        if (_dimensionShape is not { } shape || _dimensionBefore is not { } before)
        {
            return false;
        }

        if (_dimensionDragging)
        {
            before.ApplyTo(shape);
        }

        ResetDimensionPress();
        _context?.RequestRedraw();
        return true;
    }

    /// <summary>Gives the dimension of one wall segment its own distance and side (the same entry the DIMENSION card edits).</summary>
    private static void SetWallDimensionLine(Shape wall, int segment, double offset, bool flip)
    {
        var list = (wall.WallDimensionOverrides ?? []).ToList();
        var current = list.FirstOrDefault(o => o.Segment == segment) ?? new WallDimensionOverride(segment);
        list.RemoveAll(o => o.Segment == segment);
        list.Add(current with { Offset = offset, Flip = flip });
        wall.WallDimensionOverrides = list.OrderBy(o => o.Segment).ToList();
    }
}
