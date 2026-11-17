using System.Globalization;
using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Mobile.Views;

/// <summary>The Curve tool, like the fillet of a CAD program (as on Windows): tap one side of a corner, tap the neighbouring side, type the size - and
/// only that corner is curved (<see cref="CornerFillet"/>, <see cref="Icono.Editor.EditorContext.FilletCorner"/>). The size is asked for like a length.</summary>
public sealed partial class DocumentCanvas
{
    private (Shape Shape, int Side)? _curveFirst;
    private (Shape Shape, int Side)? _curveSecond;
    private int _curveCorner;

    /// <summary>Forgets the sides that were tapped (another tool was chosen).</summary>
    public void CancelCurve()
    {
        if (_curveFirst is null && _curveSecond is null)
        {
            return;
        }

        _curveFirst = null;
        _curveSecond = null;
        _context?.RequestRedraw();
    }

    /// <summary>A tap with the Curve tool: the first picks a side of a corner, the second the side next to it (and asks for the size).</summary>
    private void CurveTapped(Avalonia.Point view)
    {
        if (_context is not { } context)
        {
            return;
        }

        var point = ToDocument(view);
        var at = new SKPoint((float)point.X, (float)point.Y);
        var tolerance = (float)(FingerReach / _zoom);

        // a size box that was closed without an answer left its two sides: a new tap starts again
        if (_curveSecond is not null)
        {
            _curveFirst = null;
            _curveSecond = null;
        }

        if (_curveFirst is not { } first)
        {
            var shape = ShapeHitTester.Find(context.Document, point.X, point.Y, _zoom, FingerReach)?.Shape;
            if (shape is null)
            {
                context.Notify("Curve: tap one side of the corner (on the line itself).");
                return;
            }

            if (shape.Locked || LayerOf(shape)?.Locked == true)
            {
                context.Notify("That object is locked.");
                return;
            }

            if (CornerFillet.NearestSide(shape, at, tolerance) is not { } side)
            {
                context.Notify(ShapeGeometryFactory.CanRoundCorners(shape)
                    ? "Curve: tap one side of the corner (on the line itself)."
                    : "This object has no corners to curve: use a line with three points or more, or a shape with straight sides.");
                return;
            }

            _curveFirst = (shape, side);
            context.Notify("Curve: now tap the neighbouring side of the corner.");
            context.RequestRedraw();
            return;
        }

        if (CornerFillet.NearestSide(first.Shape, at, tolerance) is not { } second)
        {
            context.Notify("Curve: tap the side next to the first one (on the same object).");
            return;
        }

        var vertices = CornerFillet.Vertices(first.Shape);
        var corner = vertices is null ? null : CornerFillet.SharedCorner(vertices.Count, CornerFillet.IsClosed(first.Shape), first.Side, second);
        if (corner is not { } found)
        {
            context.Notify("These two sides do not meet in a corner. Tap the side right next to the first one.");
            return;
        }

        _curveSecond = (first.Shape, second);
        _curveCorner = found;
        context.RequestRedraw();

        var picked = first.Shape;
        LengthRequested?.Invoke(this, new LengthRequest(
            "Curve size",
            "Radius of the corner in cm on the page. 0 makes the corner sharp again.",
            context.CurveSizeCm.ToString("0.##", CultureInfo.InvariantCulture),
            text =>
            {
                _curveFirst = null;
                _curveSecond = null;
                if (text is null || !double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var cm) || cm < 0)
                {
                    context.Notify("That is not a size. Type a number in cm, for example 1.5.");
                    context.RequestRedraw();
                    return;
                }

                if (cm > 0)
                {
                    context.CurveSizeCm = cm;
                }

                context.FilletCorner(picked, found, cm);
            }));
    }

    /// <summary>The sides that were tapped and their corner, in screen units (to show them).</summary>
    private (IReadOnlyList<(SKPoint From, SKPoint To)> Sides, SKPoint? Corner) CurvePicksForRender()
    {
        var sides = new List<(SKPoint, SKPoint)>();
        SKPoint? corner = null;
        if (_context is not { ActiveTool: Icono.Editor.ActiveTool.CornerCurve })
        {
            return (sides, null);
        }

        var picks = new[] { (Pick: _curveFirst, Second: false), (Pick: _curveSecond, Second: true) };
        foreach (var (pick, isSecond) in picks)
        {
            if (pick is not { } p || CornerFillet.Vertices(p.Shape) is not { } vertices
                || p.Side >= CornerFillet.SideCount(vertices.Count, CornerFillet.IsClosed(p.Shape)))
            {
                continue;
            }

            var (a, b) = CornerFillet.Ends(vertices, p.Side);
            sides.Add((ToScreen((a.X, a.Y)), ToScreen((b.X, b.Y))));
            if (isSecond && _curveCorner < vertices.Count)
            {
                corner = ToScreen((vertices[_curveCorner].X, vertices[_curveCorner].Y));
            }
        }

        return (sides, corner);
    }
}
