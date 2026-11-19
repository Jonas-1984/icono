using Avalonia;
using Icono.Core;
using Icono.Editor;
using Icono.Editor.Undo;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Mobile.Views;

/// <summary>What to type: the title, a hint, the text to start with and what OK does with the typed text.</summary>
public sealed record LengthRequest(string Title, string Note, string Text, Action<string?> Commit);

/// <summary>Tap the number of a wall's dimension (or of a door / window) and type the length you want - the wall is made that long (see
/// <see cref="LengthEditing"/>, shared with the Windows program). A tap on the number wins over a tap on the wall below it.</summary>
public sealed partial class DocumentCanvas
{
    /// <summary>How far (DIP) from a dimension number a finger still takes it.</summary>
    private const double NumberReach = 10;

    /// <summary>The dimension number under the finger when it went down (wall segment or door / window), or null.</summary>
    private (Shape Shape, Layer Layer, int Segment, SymbolDimension? Symbol)? _pressNumber;

    /// <summary>The dimension number that was tapped last (a second tap on it asks for the length).</summary>
    private (Shape Shape, int Segment)? _lastTapped;

    /// <summary>Raised when the user tapped a dimension number: the window asks for the length.</summary>
    public event EventHandler<LengthRequest>? LengthRequested;

    /// <summary>The dimension number at a point of the drawing (walls first, then doors and windows), or null. Hidden and locked layers and
    /// locked objects are skipped.</summary>
    private (Shape Shape, Layer Layer, int Segment, SymbolDimension? Symbol)? FindDimensionNumber(Point at)
    {
        if (_context is not { } context)
        {
            return null;
        }

        var reach = NumberReach / _zoom;
        foreach (var layer in context.Document.Layers.Where(l => l.Visible && !l.Locked))
        {
            foreach (var wall in layer.Shapes.Where(s => s.Kind == ShapeKind.Wall && !s.Locked))
            {
                if (WallRenderer.HitTestDimensionText(wall, at.X, at.Y, reach) is { } segment)
                {
                    return (wall, layer, segment, null);
                }
            }
        }

        foreach (var layer in context.Document.Layers.Where(l => l.Visible && !l.Locked))
        {
            foreach (var symbol in layer.Shapes.Where(s => ArchitecturalSymbols.IsSymbol(s.Kind) && !s.Locked))
            {
                if (SymbolMeasurements.HitTestText(context.Document, symbol, at.X, at.Y, reach) is { } dimension)
                {
                    return (symbol, layer, -1, dimension);
                }
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- drag the number: move the dimension line

    private (Shape Shape, int Segment, ShapeSnapshot Before, SKPoint? Direction)? _dimensionDrag;

    /// <summary>The dashed line along the dimension line the dragged one has lined up with (drawing units), or null.</summary>
    private (SKPoint From, SKPoint To)? _dimensionGuide;

    /// <summary>A drag that started on a dimension number: the dimension line follows the finger (on either side of the wall, at any distance) and
    /// lines up with the dimension lines of other dimensions that run the same way. True when it began.</summary>
    private bool BeginDimensionDrag()
    {
        if (_pressNumber is not { } number || _context is not { } context)
        {
            return false;
        }

        Select(number.Shape, number.Layer);
        if (number.Symbol is null)
        {
            context.PickedDimensionSegment = number.Segment; // the DIMENSION settings of the wall show this one
        }

        _dimensionDrag = (number.Shape, number.Segment, ShapeSnapshot.Capture(number.Shape),
            DimensionLineEditing.Direction(number.Shape, number.Segment, number.Symbol));
        context.RequestRedraw();
        return true;
    }

    private void UpdateDimensionDrag(Point view)
    {
        if (_dimensionDrag is not { } drag || _context is not { } context)
        {
            return;
        }

        var at = ToDocument(view);
        var pointer = DimensionLineEditing.Snapped(
            context.Document, drag.Shape, drag.Segment, new SKPoint((float)at.X, (float)at.Y), drag.Direction, (float)(8 / _zoom), out var snap);
        _dimensionGuide = snap is null ? null : (snap.From, snap.To);
        DimensionLineEditing.Place(context.Document, drag.Shape, drag.Segment, pointer);
        context.RequestRedraw();
    }

    /// <summary>The finger lifted: the line stays where it is (one undo step).</summary>
    private void FinishDimensionDrag()
    {
        var drag = _dimensionDrag;
        _dimensionDrag = null;
        _dimensionGuide = null;
        if (drag is not { } done || _context is not { } context)
        {
            return;
        }

        var after = ShapeSnapshot.Capture(done.Shape);
        if (!after.Equals(done.Before))
        {
            context.UndoManager.Push(UndoableAction.ForPropertyEdit([done.Shape], [done.Before], [after]));
        }

        context.RequestRedraw();
    }

    /// <summary>A second finger came down: the line jumps back to where it was.</summary>
    private void CancelDimensionDrag()
    {
        if (_dimensionDrag is { } drag)
        {
            drag.Before.ApplyTo(drag.Shape);
        }

        _dimensionDrag = null;
        _dimensionGuide = null;
        _context?.RequestRedraw();
    }

    /// <summary>The guide line in screen units for the render thread, or null.</summary>
    private (SKPoint From, SKPoint To)? DimensionGuideForRender() =>
        _dimensionGuide is { } guide ? (ToScreen((guide.From.X, guide.From.Y)), ToScreen((guide.To.X, guide.To.Y))) : null;

    /// <summary>The finger lifted on a dimension number: select its object and ask for the length.</summary>
    private void TapNumber((Shape Shape, Layer Layer, int Segment, SymbolDimension? Symbol) number)
    {
        if (_context is not { } context)
        {
            return;
        }

        // the first tap SELECTS the dimension (its colour, font and the other settings are in the Properties); a second tap on the same number asks for its length
        var again = _lastTapped is { } last && ReferenceEquals(last.Shape, number.Shape) && last.Segment == number.Segment
                    && context.SelectedShapes.Count == 1 && ReferenceEquals(context.SelectedShapes[0], number.Shape);
        _lastTapped = (number.Shape, number.Segment);
        Select(number.Shape, number.Layer);
        if (number.Symbol is null)
        {
            context.PickedDimensionSegment = number.Segment; // the DIMENSION settings of the wall show this one
        }

        context.RequestRedraw();
        if (!again)
        {
            context.Notify("Dimension selected - its settings are in the Properties. Tap the number again to type its length.");
            return;
        }

        if (number.Symbol is { } dimension)
        {
            var symbol = number.Shape;
            var part = dimension.Part;
            LengthRequested?.Invoke(this, new LengthRequest(
                LengthEditing.SymbolTitle(part), LengthEditing.SymbolNote(part), LengthEditing.SymbolText(symbol, dimension),
                text => LengthEditing.CommitSymbol(context, symbol, part, text)));
            return;
        }

        var wall = number.Shape;
        var segment = number.Segment;
        context.PickedDimensionSegment = segment; // the DIMENSION settings of the wall show this one
        LengthRequested?.Invoke(this, new LengthRequest(
            "Wall length", LengthEditing.WallNote, LengthEditing.WallText(wall, segment),
            text => LengthEditing.CommitWall(context, wall, segment, text)));
    }
}
