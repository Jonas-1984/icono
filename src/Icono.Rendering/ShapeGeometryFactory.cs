using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>
/// Builds an SKPath per ShapeKind, mirroring the hand-authored 24x24 icon geometries in
/// Icono.App/Themes/ShapeIcons.xaml. Each shape is built in that same local 24x24 space,
/// then scaled/offset to the shape's real Width/Height/X/Y in one step in <see cref="Build"/>.
/// </summary>
public static class ShapeGeometryFactory
{
    private static readonly HashSet<ShapeKind> StrokeOnlyKinds =
    [
        ShapeKind.Line, ShapeKind.Arrow, ShapeKind.DoubleArrow,
    ];

    public static bool IsStrokeOnly(ShapeKind kind) => StrokeOnlyKinds.Contains(kind);

    public static SKPath Build(Shape shape)
    {
        var path = BuildLocal(shape.Kind);
        var sx = (float)(shape.Width / 24.0);
        var sy = (float)(shape.Height / 24.0);
        path.Transform(SKMatrix.CreateScale(sx, sy));
        path.Offset((float)shape.X, (float)shape.Y);
        return path;
    }

    private static SKPath BuildLocal(ShapeKind kind)
    {
        var path = new SKPath();

        switch (kind)
        {
            case ShapeKind.Rectangle:
                path.AddRect(SKRect.Create(3, 5, 18, 14));
                break;
            case ShapeKind.RoundedRectangle:
                path.AddRoundRect(SKRect.Create(3, 5, 18, 14), 4, 4);
                break;
            case ShapeKind.Ellipse:
                path.AddOval(SKRect.Create(3, 5, 18, 14));
                break;
            case ShapeKind.Triangle:
                AddPolygon(path, (12, 4), (21, 20), (3, 20));
                break;
            case ShapeKind.Diamond:
                AddPolygon(path, (12, 3), (21, 12), (12, 21), (3, 12));
                break;
            case ShapeKind.Pentagon:
                AddPolygon(path, (12, 3), (21, 9.5f), (17.5f, 20), (6.5f, 20), (3, 9.5f));
                break;
            case ShapeKind.Hexagon:
                AddPolygon(path, (8, 3), (16, 3), (21, 12), (16, 21), (8, 21), (3, 12));
                break;
            case ShapeKind.Star:
                AddPolygon(path,
                    (12, 1), (15.09f, 8.26f), (22.9f, 9.27f), (17, 14.97f), (18.18f, 22.72f),
                    (12, 18.9f), (5.82f, 22.72f), (7, 14.97f), (1.09f, 9.27f), (8.91f, 8.26f));
                break;
            case ShapeKind.Parallelogram:
                AddPolygon(path, (8, 6), (22, 6), (16, 18), (2, 18));
                break;
            case ShapeKind.Trapezoid:
                AddPolygon(path, (8, 6), (16, 6), (21, 18), (3, 18));
                break;
            case ShapeKind.Cross:
                AddPolygon(path,
                    (9, 3), (15, 3), (15, 9), (21, 9), (21, 15), (15, 15), (15, 21), (9, 21),
                    (9, 15), (3, 15), (3, 9), (9, 9));
                break;
            case ShapeKind.HalfCircle:
                path.MoveTo(3, 15);
                path.ArcTo(9, 9, 0, SKPathArcSize.Small, SKPathDirection.Clockwise, 21, 15);
                path.Close();
                break;
            case ShapeKind.Ring:
                path.FillType = SKPathFillType.EvenOdd;
                path.AddOval(SKRect.Create(3, 3, 18, 18));
                path.AddOval(SKRect.Create(8, 8, 8, 8));
                break;
            case ShapeKind.SpeechBubble:
                AddPolygon(path, (3, 4), (21, 4), (21, 16), (10, 16), (6, 21), (6, 16), (3, 16));
                break;
            case ShapeKind.Line:
                path.MoveTo(4, 20);
                path.LineTo(20, 4);
                break;
            case ShapeKind.Arrow:
                path.MoveTo(3, 12); path.LineTo(18, 12);
                path.MoveTo(13, 6); path.LineTo(19, 12); path.LineTo(13, 18);
                break;
            case ShapeKind.DoubleArrow:
                path.MoveTo(2, 12); path.LineTo(9, 6);
                path.MoveTo(2, 12); path.LineTo(9, 18);
                path.MoveTo(2, 12); path.LineTo(22, 12);
                path.MoveTo(22, 12); path.LineTo(15, 6);
                path.MoveTo(22, 12); path.LineTo(15, 18);
                break;
            case ShapeKind.Text:
                path.AddRect(SKRect.Create(3, 5, 18, 14));
                break;
            default:
                path.AddRect(SKRect.Create(3, 5, 18, 14));
                break;
        }

        return path;
    }

    private static void AddPolygon(SKPath path, params (float X, float Y)[] points)
    {
        path.MoveTo(points[0].X, points[0].Y);
        for (var i = 1; i < points.Length; i++)
        {
            path.LineTo(points[i].X, points[i].Y);
        }

        path.Close();
    }
}
