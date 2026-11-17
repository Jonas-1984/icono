using Avalonia;
using Avalonia.Input;
using Icono.Core;
using Icono.Editor;
using SkiaSharp;

namespace Icono.Mobile.Views;

/// <summary>The Pen tool (handwriting): write or draw with a pen or a finger. Every stroke - from putting the pen down to lifting it - is one object in
/// the pen colour and width of the Properties panel (<see cref="InkEditing"/>, shared with the Windows program); the tool stays on for the next
/// stroke. A pen is followed through every point it reports, so fast strokes stay round. With a pen in contact a hand resting on the screen is
/// ignored; with a finger a second finger takes the stroke back and zooms / moves the view as usual.</summary>
public sealed partial class DocumentCanvas
{
    private List<SKPoint>? _inkPoints;
    private Shape? _inkShape;
    private bool _inkWithPen;

    /// <summary>True when a touch (the palm) should be ignored because a pen is writing.</summary>
    private bool PalmShouldBeIgnored(PointerPressedEventArgs e) =>
        _gesture == Gesture.Ink && _inkWithPen && e.Pointer.Type == PointerType.Touch;

    private void BeginInk(Point document)
    {
        _inkPoints = [new SKPoint((float)document.X, (float)document.Y)];
        _inkWithPen = _pointerTypes.Values.Contains(PointerType.Pen);
        UpdateInkPreview();
    }

    /// <summary>Adds every point the pen went through since the last event (a fast stroke delivers several at once).</summary>
    private void ContinueInk(PointerEventArgs e)
    {
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

    private void UpdateInkPreview()
    {
        if (_context is { } context && _inkPoints is { } points)
        {
            _inkShape = InkEditing.Preview(context, points, _zoom);
            context.RequestRedraw();
        }
    }

    /// <summary>The pen is lifted: the stroke becomes an object (one undo step).</summary>
    private void FinishInk()
    {
        var points = _inkPoints;
        ResetInk();
        if (_context is { } context && points is not null)
        {
            InkEditing.Commit(context, points, _zoom);
            context.RequestRedraw();
        }
    }

    /// <summary>Drops the stroke that is being drawn (a second finger came down: that is a zoom).</summary>
    private void CancelInk()
    {
        ResetInk();
        _context?.RequestRedraw();
    }

    private void ResetInk()
    {
        _inkPoints = null;
        _inkShape = null;
        _inkWithPen = false;
    }
}
