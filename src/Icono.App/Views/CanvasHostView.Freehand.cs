using System.Windows;
using System.Windows.Input;
using Icono.App.State;
using Icono.App.State.Undo;
using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.App.Views;

/// <summary>The Handwriting tool: write or draw with a pen, a finger on a touch screen or the mouse. Every stroke (from putting the pen down to lifting it)
/// is one object on the page, in the pen colour and width chosen in the HANDWRITING card; the tool stays on so the next stroke can follow.</summary>
public partial class CanvasHostView
{
    private List<SKPoint>? _freehandPoints;
    private Shape? _freehandShape;

    /// <summary>A pen or a finger must not start the gestures of Windows (press and hold = right click, flicks, the touch feedback rings) while it writes.</summary>
    private void ConfigureInkInput()
    {
        Stylus.SetIsPressAndHoldEnabled(this, false);
        Stylus.SetIsFlicksEnabled(this, false);
        Stylus.SetIsTapFeedbackEnabled(this, false);
        Stylus.SetIsTouchFeedbackEnabled(this, false);
    }

    /// <summary>Forgets the stroke that is being drawn and lets go of the mouse.</summary>
    private void ResetFreehand()
    {
        _freehandPoints = null;
        _freehandShape = null;
        ReleaseMouseCapture();
    }

    private void BeginFreehand(Point point)
    {
        if (_context is null)
        {
            return;
        }

        _freehandPoints = [new SKPoint((float)point.X, (float)point.Y)];
        CaptureMouse();
        UpdateFreehandPreview();
    }

    /// <summary>Adds every point the pen went through since the last event (a fast stroke delivers several at once).</summary>
    private void ContinueFreehand(MouseEventArgs e)
    {
        if (_freehandPoints is not { Count: > 0 } points)
        {
            return;
        }

        // a pen delivers all the points it went through since the last event: they keep fast strokes round instead of angular
        var minimum = 1.2 / Zoom;
        var positions = e.StylusDevice is { } pen
            ? pen.GetStylusPoints(SkiaCanvas).Select(p => new Point(p.X, p.Y)).ToList()
            : [e.GetPosition(SkiaCanvas)];
        foreach (var position in positions)
        {
            var at = ToDocumentSpace(position);
            var last = points[^1];
            if (Math.Abs(at.X - last.X) >= minimum || Math.Abs(at.Y - last.Y) >= minimum)
            {
                points.Add(new SKPoint((float)at.X, (float)at.Y));
            }
        }

        UpdateFreehandPreview();
    }

    private void UpdateFreehandPreview()
    {
        if (_context is null || _freehandPoints is null)
        {
            return;
        }

        _freehandShape = FreehandBuilder.CreateShape(_freehandPoints, _context.PenColor, _context.PenWidth, (float)(0.5 / Zoom));
        _context.RequestRedraw();
    }

    /// <summary>The pen is lifted: the stroke becomes an object (one undo step).</summary>
    private void EndFreehand()
    {
        if (_context is null || _freehandPoints is null)
        {
            return;
        }

        var points = _freehandPoints;
        ResetFreehand();

        var layer = _context.SelectedLayer is { Locked: false } selected ? selected : _context.Document.Layers.FirstOrDefault(l => !l.Locked);
        if (layer is null)
        {
            _context.Notify("All layers are locked.");
            return;
        }

        var shape = FreehandBuilder.CreateShape(points, _context.PenColor, _context.PenWidth, (float)Math.Max(0.2, 0.8 / Zoom));
        if (shape is not null)
        {
            layer.Shapes.Insert(0, shape);
            _context.UndoManager.Push(UndoableAction.ForInsert(layer.Shapes, shape, 0));
        }

        _context.RequestRedraw();
    }

    /// <summary>Drops the stroke that is being drawn (Esc). True when there was one.</summary>
    public bool CancelFreehand()
    {
        var had = _freehandPoints is not null;
        if (had)
        {
            ResetFreehand();
            _context?.RequestRedraw();
        }

        return had;
    }
}
