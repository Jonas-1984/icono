using Avalonia;
using Avalonia.Input;
using Icono.Core;
using Icono.Editor;
using SkiaSharp;

namespace Icono.Mobile.Views;

/// <summary>The Pen tool (handwriting): write or draw with a pen or a finger. Every stroke - from putting the pen down to lifting it - is one object in
/// the pen colour and width of the Properties panel (<see cref="InkEditing"/>, shared with the Windows program); the tool stays on for the next
/// stroke. A pen is followed through every point it reports, so fast strokes stay round. With a pen in contact a hand resting on the screen is
/// ignored; with a finger a second finger takes the stroke back and zooms / moves the view as usual.
/// With "Eraser" switched on in the Properties (or with the eraser end of a pen) the touch deletes the handwriting strokes it passes over instead -
/// only strokes, never walls, furniture or texts - and a whole drag is one undo step.</summary>
public sealed partial class DocumentCanvas
{
    private List<SKPoint>? _inkPoints;
    private Shape? _inkShape;
    private bool _inkWithPen;

    // erasing
    private bool _inkErasing;
    private bool _pointerIsEraserEnd;
    private List<(Layer Layer, Shape Shape, int Index)> _erased = [];
    private Point? _eraserAt;

    /// <summary>True when a touch (the palm) should be ignored because a pen is writing.</summary>
    private bool PalmShouldBeIgnored(PointerPressedEventArgs e) =>
        _gesture == Gesture.Ink && _inkWithPen && e.Pointer.Type == PointerType.Touch;

    private void BeginInk(Point document)
    {
        _inkWithPen = _pointerTypes.Values.Contains(PointerType.Pen);
        _inkErasing = _context?.PenEraser == true || _pointerIsEraserEnd;
        if (_inkErasing)
        {
            _erased = [];
            EraseAt(document);
            return;
        }

        _inkPoints = [new SKPoint((float)document.X, (float)document.Y)];
        UpdateInkPreview();
    }

    /// <summary>Adds every point the pen went through since the last event (a fast stroke delivers several at once).</summary>
    private void ContinueInk(PointerEventArgs e)
    {
        if (_inkErasing)
        {
            foreach (var sample in e.GetIntermediatePoints(this))
            {
                EraseAt(ToDocument(sample.Position));
            }

            return;
        }

        if (_inkPoints is not { Count: > 0 } points)
        {
            return;
        }

        var minimum = 1.2 / _zoom;
        foreach (var sample in e.GetIntermediatePoints(this))
        {
            var at = ToDocument(sample.Position);
            var last = points[^1];
            if (Math.Abs(at.X - last.X) >= minimum || Math.Abs(at.Y - last.Y) >= minimum)
            {
                points.Add(new SKPoint((float)at.X, (float)at.Y));
            }
        }

        UpdateInkPreview();
    }

    private void EraseAt(Point document)
    {
        _eraserAt = document;
        if (_context is { } context)
        {
            InkEditing.EraseAt(context, document.X, document.Y, _zoom, _erased);
        }

        InvalidateVisual();
    }

    private void UpdateInkPreview()
    {
        if (_context is { } context && _inkPoints is { } points)
        {
            _inkShape = InkEditing.Preview(context, points, _zoom);
            context.RequestRedraw();
        }
    }

    /// <summary>The pen is lifted: the stroke becomes an object (one undo step), or what the eraser deleted is kept as one undo step.</summary>
    private void FinishInk()
    {
        var points = _inkPoints;
        var erased = _erased;
        var erasing = _inkErasing;
        ResetInk();
        if (_context is not { } context)
        {
            return;
        }

        if (erasing)
        {
            InkEditing.CommitErase(context, erased);
        }
        else if (points is not null)
        {
            InkEditing.Commit(context, points, _zoom);
        }

        context.RequestRedraw();
    }

    /// <summary>Drops the stroke that is being drawn, or puts back what the eraser deleted (a second finger came down: that is a zoom).</summary>
    private void CancelInk()
    {
        if (_inkErasing && _context is { } context)
        {
            InkEditing.RestoreErased(context, _erased);
        }

        ResetInk();
        _context?.RequestRedraw();
    }

    private void ResetInk()
    {
        _inkPoints = null;
        _inkShape = null;
        _inkWithPen = false;
        _inkErasing = false;
        _erased = [];
        _eraserAt = null;
        InvalidateVisual();
    }

    /// <summary>The ring that shows where the eraser works, in screen units for the render thread (null when not erasing).</summary>
    private (SKPoint At, float Radius)? EraserForRender() =>
        _inkErasing && _eraserAt is { } at ? (ToScreen((at.X, at.Y)), (float)InkEditing.EraserReachPx) : null;
}
