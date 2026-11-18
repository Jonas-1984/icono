using Avalonia;
using Icono.Core;
using Icono.Editor;
using Icono.Editor.Undo;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Mobile.Views;

/// <summary>The other tools of the Windows program, with the finger: Match (copy a look), the colour picker, Add Point and Delete Point, Cut Wall,
/// the ruler (drag it: nothing is added) and the distance between two objects. A tap does it; the logic is the shared one (EditorContext,
/// PointEditing, WallEditor, DimensionBuilder).</summary>
public sealed partial class DocumentCanvas
{
    private Point? _rulerStart;
    private Point _rulerEnd;
    private bool _rulerFrozen;
    private Shape? _gapFirst;

    /// <summary>The tools that act on a tap (the ruler is a drag, see <see cref="BeginRuler"/>).</summary>
    private static bool IsTapTool(ActiveTool tool) =>
        tool is ActiveTool.Match or ActiveTool.Eyedropper or ActiveTool.AddPoint or ActiveTool.DeletePoint or ActiveTool.WallCut or ActiveTool.MeasureGap;

    /// <summary>Forgets what a tool had begun (another tool was chosen): sides for the Curve tool, the ruler, a first object for a distance, the
    /// source of Match.</summary>
    public void ResetToolState()
    {
        CancelCurve();
        _rulerStart = null;
        _rulerFrozen = false;
        _gapFirst = null;
        if (_context is { MatchSource: not null } context)
        {
            context.MatchSource = null;
        }

        _context?.RequestRedraw();
    }

    /// <summary>A finger was put down, lifted without moving much, with one of the tap tools.</summary>
    private void ToolTapped(Point view)
    {
        if (_context is not { } context)
        {
            return;
        }

        var at = ToDocument(view);
        switch (context.ActiveTool)
        {
            case ActiveTool.Match:
                MatchTapped(context, at);
                break;
            case ActiveTool.Eyedropper:
                PickColorAt(context, at);
                break;
            case ActiveTool.AddPoint:
            case ActiveTool.DeletePoint:
                PointToolTapped(context, at, add: context.ActiveTool == ActiveTool.AddPoint);
                break;
            case ActiveTool.WallCut:
                WallCutTapped(context, at);
                break;
            case ActiveTool.MeasureGap:
                GapTapped(context, at);
                break;
        }

        context.RequestRedraw();
    }

    // ---------------------------------------------------------------- Match

    private void MatchTapped(EditorContext context, Point at)
    {
        var number = FindDimensionNumber(at);
        var hit = number is null ? ShapeHitTester.Find(context.Document, at.X, at.Y, _zoom, FingerReach)?.Shape : null;
        var shape = number?.Shape ?? hit;
        if (shape is null)
        {
            // a tap beside everything ends the tool
            context.MatchSource = null;
            context.ActiveTool = ActiveTool.Select;
            context.Notify("Match ended.");
            return;
        }

        var dimensionOnly = number is not null;
        int? segment = number is { Segment: >= 0 } n ? n.Segment : null;
        if (context.MatchSource is null || (ReferenceEquals(shape, context.MatchSource) && !dimensionOnly))
        {
            context.PickMatchSource(shape, segment, dimensionOnly);
            context.Notify(dimensionOnly
                ? "Match: this dimension is the source - tap the numbers of other dimensions (or objects) that should get its style. Tap beside everything to end."
                : "Match: source picked - tap the objects that should get its look. Tap the source again to pick another. Tap beside everything to end.");
            return;
        }

        context.MatchTo(shape, dimensionOnly, segment);
    }

    // ---------------------------------------------------------------- colour picker

    /// <summary>The colour of the drawing at the tap becomes the colour new shapes get (the same as in the Windows program).</summary>
    private static void PickColorAt(EditorContext context, Point at)
    {
        var document = context.Document;
        var x = (int)Math.Floor(at.X);
        var y = (int)Math.Floor(at.Y);
        if (x < 0 || y < 0 || x >= document.Width || y >= document.TotalHeight)
        {
            return;
        }

        using var bitmap = DocumentExporter.RenderBitmap(document, document.Width, document.TotalHeight, includeBackground: true);
        var pixel = bitmap.GetPixel(x, y);
        if (pixel.Alpha == 0)
        {
            context.Notify("There is no colour there - tap on a coloured spot.");
            return;
        }

        context.CurrentFillColor = new ArgbColor(pixel.Alpha, pixel.Red, pixel.Green, pixel.Blue);
        context.ActiveTool = ActiveTool.Select;
        context.Notify($"Colour #{pixel.Red:X2}{pixel.Green:X2}{pixel.Blue:X2} picked - the next shapes get it.");
    }

    // ---------------------------------------------------------------- Add Point / Delete Point

    private void PointToolTapped(EditorContext context, Point at, bool add)
    {
        var reach = FingerReach / _zoom;
        if (context.SelectedShapes.Count == 1 && context.SelectedShapes[0] is { Points: not null } shape)
        {
            var done = add
                ? PointEditing.InsertPointNear(context, shape, at.X, at.Y, reach)
                : PointEditing.RemovePointNear(context, shape, at.X, at.Y, reach);
            if (done)
            {
                return;
            }
        }

        // a tap on another object chooses it (its points are edited by the next tap)
        if (ShapeHitTester.Find(context.Document, at.X, at.Y, _zoom, FingerReach) is { } hit && !ReferenceEquals(hit.Shape, context.SelectedShapes.FirstOrDefault()))
        {
            Select(hit.Shape, hit.Layer);
            context.Notify(add ? "Add point: now tap on a side of it." : "Delete point: now tap on the point to take out.");
            return;
        }

        context.Notify(add
            ? "Add point: tap on a side of the selected line, wall or shape of points."
            : "Delete point: tap on a point of the selected shape (at least two stay).");
    }

    // ---------------------------------------------------------------- Cut Wall

    private void WallCutTapped(EditorContext context, Point at)
    {
        var reach = FingerReach / _zoom;
        foreach (var layer in context.Document.Layers.Where(l => l.Visible && !l.Locked))
        {
            foreach (var wall in layer.Shapes.Where(s => s.Kind == ShapeKind.Wall && s.Points is not null && !s.Locked).ToList())
            {
                if (WallEditor.Nearest(wall, at.X, at.Y, reach) is not { } spot || spot.Distance > wall.WallThickness / 2 + reach / 2)
                {
                    continue;
                }

                var parts = WallEditor.SplitAt(wall, spot);
                if (parts.Count > 0)
                {
                    context.ReplaceShape(wall, parts);
                    context.Notify("Wall cut in two.");
                    return;
                }
            }
        }

        context.Notify("Cut wall: tap on a wall where it is to be cut.");
    }

    // ---------------------------------------------------------------- distance between two objects

    private void GapTapped(EditorContext context, Point at)
    {
        var hit = HitTestMeasurable(context, at);
        if (hit is null)
        {
            context.Notify(_gapFirst is null ? "Distance: tap the first object." : "Distance: tap the second object.");
            return;
        }

        if (_gapFirst is null)
        {
            _gapFirst = hit;
            context.Notify("Distance: now tap the object to measure to.");
            return;
        }

        if (ReferenceEquals(hit, _gapFirst))
        {
            return;
        }

        var dimension = DimensionBuilder.CreateGap(_gapFirst, hit, context.DimensionUnit);
        _gapFirst = null;
        var layer = context.SelectedLayer ?? context.Document.Layers.FirstOrDefault();
        if (dimension is null || layer is null)
        {
            context.Notify("Distance: these two objects have no gap to measure.");
            return;
        }

        layer.Shapes.Insert(0, dimension);
        context.UndoManager.Push(UndoableAction.ForInsert(layer.Shapes, dimension, 0));
        context.Notify("Distance dimension added. It follows both objects.");
    }

    /// <summary>The object under the finger that can be measured (a wall on its band, everything else on its outline).</summary>
    private Shape? HitTestMeasurable(EditorContext context, Point at)
    {
        var tolerance = FingerReach / _zoom / 2;
        foreach (var layer in context.Document.Layers.Where(l => l.Visible))
        {
            foreach (var shape in layer.Shapes)
            {
                if (!OutlineGeometry.IsMeasurable(shape))
                {
                    continue;
                }

                if (shape.Kind == ShapeKind.Wall)
                {
                    if (WallEditor.Nearest(shape, at.X, at.Y, 0) is { } spot && spot.Distance <= shape.WallThickness / 2 + tolerance)
                    {
                        return shape;
                    }

                    continue;
                }

                if (OutlineGeometry.HitTest(shape, at.X, at.Y, tolerance))
                {
                    return shape;
                }
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- the ruler

    private void BeginRuler(Point at)
    {
        var start = SnapToObjectPoints(at, exclude: []) ?? at;
        _rulerStart = start;
        _rulerEnd = start;
        _rulerFrozen = false;
        _context?.RequestRedraw();
    }

    private void UpdateRuler(Point view)
    {
        if (_rulerStart is null || _rulerFrozen)
        {
            return;
        }

        var at = ToDocument(view);
        _rulerEnd = SnapToObjectPoints(at, exclude: [], reference: new SKPoint((float)_rulerStart.Value.X, (float)_rulerStart.Value.Y)) ?? at;
        _context?.RequestRedraw();
    }

    /// <summary>The finger lifted: the measurement stays on the screen until the next one (nothing is added to the drawing).</summary>
    private void FinishRuler()
    {
        _snapMark = null;
        _rulerFrozen = true;
        _context?.RequestRedraw();
    }

    /// <summary>The ruler as it is drawn: both ends and the text, in screen units.</summary>
    private (SKPoint From, SKPoint To, string Label)? RulerForRender()
    {
        if (_context is not { ActiveTool: ActiveTool.Measure } || _rulerStart is not { } start)
        {
            return null;
        }

        var dx = _rulerEnd.X - start.X;
        var dy = _rulerEnd.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var angle = Geo.AngleDegrees(new SKPoint((float)start.X, (float)start.Y), new SKPoint((float)_rulerEnd.X, (float)_rulerEnd.Y));
        var label = $"{DimensionRenderer.FormatAuto(length)}   ΔX {DimensionRenderer.FormatAuto(Math.Abs(dx))}   ΔY {DimensionRenderer.FormatAuto(Math.Abs(dy))}   {angle:0.#}°";
        return (ToScreen((start.X, start.Y)), ToScreen((_rulerEnd.X, _rulerEnd.Y)), label);
    }

    /// <summary>The objects that are outlined: the source of Match, the first object of a distance.</summary>
    private IReadOnlyList<Shape> HighlightsForRender()
    {
        var list = new List<Shape>();
        if (_context is { ActiveTool: ActiveTool.Match, MatchSource: { } source })
        {
            list.Add(source);
        }

        if (_context is { ActiveTool: ActiveTool.MeasureGap } && _gapFirst is { } first)
        {
            list.Add(first);
        }

        return list;
    }
}
