using Avalonia;
using Icono.Core;
using Icono.Editor;
using SkiaSharp;

namespace Icono.Mobile.Views;

/// <summary>Choosing several objects. In the "Area" mode (the Area button of the tool bar) a drag with one finger draws an area: from left to
/// right a window (only what lies completely inside is chosen), from right to left a crossing (everything it touches) - the rule of the Windows
/// program (<see cref="SelectionArea"/>). A tap adds an object to the selection or takes it out again. After an area the mode ends, so the
/// chosen objects can be moved or resized at once.</summary>
public sealed partial class DocumentCanvas
{
    private bool _areaMode;

    // the area being drawn, in drawing units
    private Point _marqueeStart;
    private Point _marqueeCurrent;
    private bool _marqueeActive;

    /// <summary>True while the Area mode is on.</summary>
    public bool AreaMode
    {
        get => _areaMode;
        set
        {
            if (_areaMode == value)
            {
                return;
            }

            _areaMode = value;
            _marqueeActive = false;
            AreaModeChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    /// <summary>Raised when the Area mode is switched on or off (also by the canvas itself, after an area).</summary>
    public event EventHandler? AreaModeChanged;

    private void BeginMarquee(Point view)
    {
        _marqueeStart = ToDocument(view);
        _marqueeCurrent = _marqueeStart;
        _marqueeActive = true;
        InvalidateVisual();
    }

    private void UpdateMarquee(Point view)
    {
        _marqueeCurrent = ToDocument(view);
        InvalidateVisual();
    }

    private void CancelMarquee()
    {
        _marqueeActive = false;
        InvalidateVisual();
    }

    private void FinishMarquee()
    {
        _marqueeActive = false;
        if (_context is not { } context)
        {
            return;
        }

        var area = MarqueeRect();
        var found = SelectionArea.Find(context.Document, area, SelectionArea.IsCrossing(_marqueeStart.X, _marqueeCurrent.X));

        // the area replaces the selection (as on Windows, where the click that starts it clears it)
        context.SelectedShapes.Clear();
        foreach (var shape in found)
        {
            context.SelectedShapes.Add(shape);
        }

        if (found.Count > 0 && context.Document.Layers.FirstOrDefault(l => l.Shapes.Contains(found[0])) is { } layer)
        {
            context.SelectedLayer = layer;
        }

        context.Notify(found.Count == 0 ? "Nothing in the area." : $"{found.Count} object(s) chosen.");
        AreaMode = false;
        context.RequestRedraw();
    }

    /// <summary>The area in drawing units.</summary>
    private SKRect MarqueeRect() => SKRect.Create(
        (float)Math.Min(_marqueeStart.X, _marqueeCurrent.X),
        (float)Math.Min(_marqueeStart.Y, _marqueeCurrent.Y),
        (float)Math.Abs(_marqueeCurrent.X - _marqueeStart.X),
        (float)Math.Abs(_marqueeCurrent.Y - _marqueeStart.Y));

    /// <summary>Adds an object (a whole group) to the selection or takes it out again.</summary>
    private void ToggleSelected(Shape shape, Layer layer)
    {
        if (_context is not { } context)
        {
            return;
        }

        var mates = shape.GroupId is { } groupId
            ? context.Document.Layers.SelectMany(l => l.Shapes).Where(s => s.GroupId == groupId).ToList()
            : [shape];

        if (context.SelectedShapes.Contains(shape))
        {
            foreach (var mate in mates)
            {
                context.SelectedShapes.Remove(mate);
            }
        }
        else
        {
            foreach (var mate in mates)
            {
                if (!context.SelectedShapes.Contains(mate))
                {
                    context.SelectedShapes.Add(mate);
                }
            }

            context.SelectedLayer = layer;
        }
    }

    /// <summary>The area being drawn in screen units for the render thread (null when none), and whether it is a crossing.</summary>
    private (SKRect Area, bool Crossing)? MarqueeForRender()
    {
        if (!_marqueeActive)
        {
            return null;
        }

        var a = ToScreen((_marqueeStart.X, _marqueeStart.Y));
        var b = ToScreen((_marqueeCurrent.X, _marqueeCurrent.Y));
        return (new SKRect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)),
            SelectionArea.IsCrossing(_marqueeStart.X, _marqueeCurrent.X));
    }
}
