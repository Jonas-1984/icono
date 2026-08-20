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
        ShapeKind.Line, ShapeKind.Arrow, ShapeKind.DoubleArrow, ShapeKind.Polyline, ShapeKind.Curve,
        ShapeKind.BentArrowRight, ShapeKind.BentArrowUp,
        ShapeKind.LoopArrowClockwise, ShapeKind.LoopArrowCounterClockwise,
        ShapeKind.StripedArrowRight,
    ];

    /// <summary>Kinds whose local geometry is closed straight-edge polygons — the only shapes
    /// <see cref="CornerRadius"/> rounding can sensibly apply to (excludes already-curved kinds
    /// like Ellipse/Ring/HalfCircle/Cloud/Gear and the open stroke-only line-family kinds).</summary>
    private static readonly HashSet<ShapeKind> CornerRoundableKinds =
    [
        ShapeKind.Rectangle, ShapeKind.RoundedRectangle, ShapeKind.Triangle, ShapeKind.Diamond,
        ShapeKind.Pentagon, ShapeKind.Hexagon, ShapeKind.Star, ShapeKind.Parallelogram,
        ShapeKind.Trapezoid, ShapeKind.Cross, ShapeKind.SpeechBubble, ShapeKind.Text,
        ShapeKind.Octagon, ShapeKind.Heart, ShapeKind.Shield, ShapeKind.Bookmark,
        ShapeKind.ArrowRight, ShapeKind.ArrowLeft, ShapeKind.ArrowUp, ShapeKind.ArrowDown,
        ShapeKind.ChevronRight, ShapeKind.ChevronLeft, ShapeKind.ChevronUp, ShapeKind.ChevronDown,
        ShapeKind.ArrowLeftRight, ShapeKind.ArrowUpDown, ShapeKind.CompassArrows,
        ShapeKind.NotchedArrowRight, ShapeKind.PlayTriangleRight,
    ];

    public static bool IsStrokeOnly(ShapeKind kind) => StrokeOnlyKinds.Contains(kind);

    /// <summary>Which fixed-geometry kinds can be converted into a freeform points-based shape via
    /// "Edit Points" — the same closed straight-edge polygon set corner rounding applies to (curved
    /// kinds like Ellipse/Gear/Cloud have no meaningful vertex list).</summary>
    public static bool SupportsPointEditing(ShapeKind kind) => CornerRoundableKinds.Contains(kind);

    /// <summary>Samples a fixed-geometry shape's own local template (ignoring rotation and any
    /// existing corner rounding) into an ordered list of shape-local vertices, for assigning to
    /// <see cref="Shape.Points"/> so the shape becomes freely vertex-editable. The result matches
    /// the shape's current un-rotated outline exactly at the moment of conversion — subsequent
    /// edits move independently from Width/Height/CornerRadius from then on.</summary>
    public static IReadOnlyList<CorePoint> ExtractEditablePoints(Shape shape)
    {
        var path = BuildLocal(shape.Kind);

        if (shape.FlipHorizontal)
        {
            path.Transform(SKMatrix.CreateScale(-1, 1, 12, 12));
        }

        if (shape.FlipVertical)
        {
            path.Transform(SKMatrix.CreateScale(1, -1, 12, 12));
        }

        var sx = (float)(shape.Width / 24.0);
        var sy = (float)(shape.Height / 24.0);
        path.Transform(SKMatrix.CreateScale(sx, sy));

        var result = new List<CorePoint>();
        foreach (var p in path.Points)
        {
            var point = new CorePoint(p.X, p.Y);
            if (result.Count == 0 || result[^1] != point)
            {
                result.Add(point);
            }
        }

        path.Dispose();
        return result;
    }

    public static SKPath Build(Shape shape)
    {
        if (shape.Kind == ShapeKind.Box3D)
        {
            var boxPath = BuildBox3DOutline(shape);

            if (shape.Rotation != 0)
            {
                var boxCenterX = (float)(shape.X + shape.Width / 2);
                var boxCenterY = (float)(shape.Y + shape.Height / 2);
                boxPath.Transform(SKMatrix.CreateRotationDegrees((float)shape.Rotation, boxCenterX, boxCenterY));
            }

            return boxPath;
        }

        if (shape.Points is { Count: > 0 } points)
        {
            var pointsPath = BuildFromPoints(shape.Kind, points, shape.X, shape.Y, shape.PathRoutingStyle, shape.CornerRadius);

            if (shape.Rotation != 0)
            {
                var rotCenterX = (float)(shape.X + shape.Width / 2);
                var rotCenterY = (float)(shape.Y + shape.Height / 2);
                pointsPath.Transform(SKMatrix.CreateRotationDegrees((float)shape.Rotation, rotCenterX, rotCenterY));
            }

            return pointsPath;
        }

        var path = shape.Kind == ShapeKind.CombinedPath
            ? (shape.CustomPathData is not null ? SKPath.ParseSvgPathData(shape.CustomPathData) ?? new SKPath() : new SKPath())
            : BuildLocal(shape.Kind);

        // Mirror in local (pre-scale) space so flipping reads the same regardless of aspect ratio.
        if (shape.FlipHorizontal)
        {
            path.Transform(SKMatrix.CreateScale(-1, 1, 12, 12));
        }

        if (shape.FlipVertical)
        {
            path.Transform(SKMatrix.CreateScale(1, -1, 12, 12));
        }

        var sx = (float)(shape.Width / 24.0);
        var sy = (float)(shape.Height / 24.0);
        path.Transform(SKMatrix.CreateScale(sx, sy));
        path.Offset((float)shape.X, (float)shape.Y);

        // Rounding runs after scale/offset (in real output px, not the 24x24 local template) so a
        // given CornerRadius reads the same regardless of the shape's aspect ratio.
        if (shape.CornerRadius > 0 && CornerRoundableKinds.Contains(shape.Kind))
        {
            var rounded = new SKPath();
            AddRoundedPolygon(rounded, (float)shape.CornerRadius, path.Points);
            path.Dispose();
            path = rounded;
        }

        if (shape.Rotation != 0)
        {
            var centerX = (float)(shape.X + shape.Width / 2);
            var centerY = (float)(shape.Y + shape.Height / 2);
            path.Transform(SKMatrix.CreateRotationDegrees((float)shape.Rotation, centerX, centerY));
        }

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
                // Same flat rect as Rectangle — its "roundedness" comes from a nonzero default
                // Shape.CornerRadius set at creation time, applied generically in Build() like
                // any other shape, rather than a hardcoded local radius.
                path.AddRect(SKRect.Create(3, 5, 18, 14));
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

            case ShapeKind.Octagon:
                AddPolygon(path, (8, 4), (16, 4), (20, 8), (20, 16), (16, 20), (8, 20), (4, 16), (4, 8));
                break;
            case ShapeKind.Heart:
                AddPolygon(path,
                    (12, 7), (9, 3), (4, 3), (2, 7), (2, 11), (12, 21), (22, 11), (22, 7), (20, 3), (15, 3));
                break;
            case ShapeKind.Cloud:
                path.MoveTo(5, 17);
                path.ArcTo(4, 4, 0, SKPathArcSize.Small, SKPathDirection.Clockwise, 9, 13);
                path.ArcTo(5, 5, 0, SKPathArcSize.Small, SKPathDirection.Clockwise, 15, 9);
                path.ArcTo(4, 4, 0, SKPathArcSize.Small, SKPathDirection.Clockwise, 19, 13);
                path.ArcTo(4, 4, 0, SKPathArcSize.Small, SKPathDirection.Clockwise, 21, 17);
                path.LineTo(5, 17);
                path.Close();
                break;
            case ShapeKind.Shield:
                AddPolygon(path, (4, 4), (20, 4), (20, 12), (12, 21), (4, 12));
                break;
            case ShapeKind.Bookmark:
                AddPolygon(path, (5, 3), (19, 3), (19, 21), (12, 15), (5, 21));
                break;
            case ShapeKind.Gear:
                AddGear(path);
                break;
            case ShapeKind.Club:
            {
                // Parsed straight from the user-supplied Lucide "club" SVG (three overlapping
                // circular lobes + a short stem), rather than hand-built from arcs, so the outline
                // matches the source icon exactly.
                using var clubPath = SKPath.ParseSvgPathData(
                    "M17.28 9.05a5.5 5.5 0 1 0-10.56 0A5.5 5.5 0 1 0 12 17.66a5.5 5.5 0 1 0 5.28-8.6Z M12 17.66L12 22");
                if (clubPath is not null)
                {
                    path.AddPath(clubPath);
                }

                break;
            }

            case ShapeKind.ArrowRight:
                AddPolygon(path, (3, 9), (14, 9), (14, 4), (21, 12), (14, 20), (14, 15), (3, 15));
                break;
            case ShapeKind.ArrowLeft:
                AddPolygon(path, (21, 9), (10, 9), (10, 4), (3, 12), (10, 20), (10, 15), (21, 15));
                break;
            case ShapeKind.ArrowUp:
                AddPolygon(path, (9, 21), (9, 10), (4, 10), (12, 3), (20, 10), (15, 10), (15, 21));
                break;
            case ShapeKind.ArrowDown:
                AddPolygon(path, (9, 3), (9, 14), (4, 14), (12, 21), (20, 14), (15, 14), (15, 3));
                break;
            case ShapeKind.ChevronRight:
                AddPolygon(path, (5, 3), (11, 3), (19, 12), (11, 21), (5, 21), (13, 12));
                break;
            case ShapeKind.ChevronLeft:
                AddPolygon(path, (19, 3), (13, 3), (5, 12), (13, 21), (19, 21), (11, 12));
                break;
            case ShapeKind.ChevronUp:
                AddPolygon(path, (3, 19), (3, 13), (12, 5), (21, 13), (21, 19), (12, 11));
                break;
            case ShapeKind.ChevronDown:
                AddPolygon(path, (3, 5), (3, 11), (12, 19), (21, 11), (21, 5), (12, 13));
                break;
            case ShapeKind.ArrowLeftRight:
                AddPolygon(path,
                    (2, 12), (7, 7), (7, 10), (17, 10), (17, 7), (22, 12),
                    (17, 17), (17, 14), (7, 14), (7, 17));
                break;
            case ShapeKind.ArrowUpDown:
                AddPolygon(path,
                    (12, 2), (17, 7), (14, 7), (14, 17), (17, 17), (12, 22),
                    (7, 17), (10, 17), (10, 7), (7, 7));
                break;
            case ShapeKind.BentArrowRight:
                path.MoveTo(5, 5); path.LineTo(5, 14); path.LineTo(17, 14);
                path.MoveTo(12, 9); path.LineTo(19, 14); path.LineTo(12, 19);
                break;
            case ShapeKind.BentArrowUp:
                path.MoveTo(19, 19); path.LineTo(10, 19); path.LineTo(10, 7);
                path.MoveTo(5, 12); path.LineTo(10, 5); path.LineTo(15, 12);
                break;
            case ShapeKind.LoopArrowClockwise:
                path.MoveTo(17, 6);
                path.ArcTo(8, 8, 0, SKPathArcSize.Large, SKPathDirection.Clockwise, 7, 18);
                path.MoveTo(4, 15); path.LineTo(7, 18); path.LineTo(10, 14);
                break;
            case ShapeKind.LoopArrowCounterClockwise:
                path.MoveTo(7, 6);
                path.ArcTo(8, 8, 0, SKPathArcSize.Large, SKPathDirection.CounterClockwise, 17, 18);
                path.MoveTo(20, 15); path.LineTo(17, 18); path.LineTo(14, 14);
                break;
            case ShapeKind.CompassArrows:
                AddPolygon(path,
                    (12, 2), (15, 6), (13, 6), (13, 10), (17, 10), (17, 8), (21, 12),
                    (17, 16), (17, 14), (13, 14), (13, 18), (15, 18), (12, 22), (9, 18),
                    (11, 18), (11, 14), (7, 14), (7, 16), (3, 12), (7, 8), (7, 10), (11, 10), (11, 6), (9, 6));
                break;
            case ShapeKind.NotchedArrowRight:
                AddPolygon(path, (6, 9), (15, 9), (15, 4), (22, 12), (15, 20), (15, 15), (6, 15), (9, 12));
                break;
            case ShapeKind.PlayTriangleRight:
                AddPolygon(path, (6, 4), (6, 20), (20, 12));
                break;
            case ShapeKind.StripedArrowRight:
                path.MoveTo(3, 12); path.LineTo(14, 12);
                path.MoveTo(9, 6); path.LineTo(15, 12); path.LineTo(9, 18);
                path.MoveTo(17, 7); path.LineTo(20, 7);
                path.MoveTo(17, 12); path.LineTo(21, 12);
                path.MoveTo(17, 17); path.LineTo(20, 17);
                break;

            default:
                path.AddRect(SKRect.Create(3, 5, 18, 14));
                break;
        }

        return path;
    }

    /// <summary>Builds the three separate face paths of a <see cref="ShapeKind.Box3D"/> — front,
    /// top, and side — so <c>ShapeRenderer</c> can shade each independently for a pseudo-3D look.
    /// Depth is clamped so the extrusion never exceeds 60% of the shorter side.</summary>
    public static (SKPath Front, SKPath Top, SKPath Side) BuildBox3DFaces(Shape shape)
    {
        var x = (float)shape.X;
        var y = (float)shape.Y;
        var w = (float)shape.Width;
        var h = (float)shape.Height;
        var d = (float)Math.Min(shape.Depth3D, Math.Min(shape.Width, shape.Height) * 0.6);

        var front = new SKPath();
        front.MoveTo(x, y + d);
        front.LineTo(x + w - d, y + d);
        front.LineTo(x + w - d, y + h);
        front.LineTo(x, y + h);
        front.Close();

        var top = new SKPath();
        top.MoveTo(x, y + d);
        top.LineTo(x + d, y);
        top.LineTo(x + w, y);
        top.LineTo(x + w - d, y + d);
        top.Close();

        var side = new SKPath();
        side.MoveTo(x + w - d, y + d);
        side.LineTo(x + w, y);
        side.LineTo(x + w, y + h - d);
        side.LineTo(x + w - d, y + h);
        side.Close();

        return (front, top, side);
    }

    private static SKPath BuildBox3DOutline(Shape shape)
    {
        var (front, top, side) = BuildBox3DFaces(shape);
        var combined = new SKPath();
        combined.AddPath(front);
        combined.AddPath(top);
        combined.AddPath(side);
        front.Dispose();
        top.Dispose();
        side.Dispose();
        return combined;
    }

    private static void AddGear(SKPath path)
    {
        const int teeth = 8;
        const float innerR = 7.5f;
        const float toothR = 11.5f;
        const float cx = 12f;
        const float cy = 12f;

        for (var i = 0; i < teeth; i++)
        {
            var a0 = i * 2 * MathF.PI / teeth;
            var a1 = a0 + MathF.PI / teeth * 0.6f;
            var a2 = a0 + MathF.PI / teeth * 1.4f;

            var p0 = new SKPoint(cx + innerR * MathF.Cos(a0), cy + innerR * MathF.Sin(a0));
            var p1 = new SKPoint(cx + toothR * MathF.Cos(a1), cy + toothR * MathF.Sin(a1));
            var p2 = new SKPoint(cx + toothR * MathF.Cos(a2), cy + toothR * MathF.Sin(a2));

            if (i == 0)
            {
                path.MoveTo(p0);
            }
            else
            {
                path.LineTo(p0);
            }

            path.LineTo(p1);
            path.LineTo(p2);
        }

        path.Close();
        path.FillType = SKPathFillType.EvenOdd;
        path.AddOval(SKRect.Create(8.5f, 8.5f, 7, 7));
    }

    /// <summary>Builds geometry for a Points-based shape (Line/Arrow/DoubleArrow/Polyline/Curve) —
    /// straight segments for the first four, a Catmull-Rom-fitted smooth curve for Curve.
    /// Dispatches BEFORE <see cref="BuildLocal"/>'s 24x24 template switch, which stays untouched
    /// for every fixed-geometry kind.</summary>
    private static SKPath BuildFromPoints(
        ShapeKind kind, IReadOnlyList<CorePoint> points, double originX, double originY,
        PathRoutingStyle routingStyle, double cornerRadius = 0)
    {
        var path = new SKPath();
        var pts = new SKPoint[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            pts[i] = new SKPoint((float)(originX + points[i].X), (float)(originY + points[i].Y));
        }

        // A converted fixed shape (Star, Cross, etc. with Points set via "Edit Points") isn't
        // stroke-only, so its outline must close back to the first point to read as a polygon.
        var closeForFill = !IsStrokeOnly(kind);

        // The dedicated Curve tool always fits a smooth curve regardless of PathRoutingStyle;
        // other Points-based kinds (Line/Arrow/DoubleArrow/Polyline) switch on it — this is the
        // Straight/Elbow/Rounded/Curved connector-style picker.
        var effectiveStyle = kind == ShapeKind.Curve ? PathRoutingStyle.Curved : routingStyle;

        if (effectiveStyle == PathRoutingStyle.Curved)
        {
            AddCatmullRomCurve(path, pts, closeForFill);
            return path;
        }

        if (effectiveStyle == PathRoutingStyle.Rounded && pts.Length >= 2)
        {
            var routed = BuildOrthogonalPoints(pts);
            var radius = (float)(cornerRadius > 0 ? cornerRadius : 8);
            AddRoundedOpenPolyline(path, radius, routed, closeForFill);

            if (routed.Count >= 2 && kind is ShapeKind.Arrow or ShapeKind.DoubleArrow)
            {
                AddArrowhead(path, routed[^2], routed[^1]);
            }

            if (routed.Count >= 2 && kind == ShapeKind.DoubleArrow)
            {
                AddArrowhead(path, routed[1], routed[0]);
            }

            return path;
        }

        IReadOnlyList<SKPoint> drawnPoints =
            effectiveStyle == PathRoutingStyle.Orthogonal && pts.Length >= 2
                ? BuildOrthogonalPoints(pts)
                : pts;

        path.MoveTo(drawnPoints[0]);
        for (var i = 1; i < drawnPoints.Count; i++)
        {
            path.LineTo(drawnPoints[i]);
        }

        if (closeForFill)
        {
            path.Close();
        }

        if (drawnPoints.Count >= 2 && kind is ShapeKind.Arrow or ShapeKind.DoubleArrow)
        {
            AddArrowhead(path, drawnPoints[^2], drawnPoints[^1]);
        }

        if (drawnPoints.Count >= 2 && kind == ShapeKind.DoubleArrow)
        {
            AddArrowhead(path, drawnPoints[1], drawnPoints[0]);
        }

        return path;
    }

    /// <summary>Inserts a right-angle bend between every consecutive pair so the route only ever
    /// moves horizontally or vertically — the "elbow" connector style.</summary>
    private static List<SKPoint> BuildOrthogonalPoints(IReadOnlyList<SKPoint> pts)
    {
        var result = new List<SKPoint> { pts[0] };

        for (var i = 0; i < pts.Count - 1; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];

            if (MathF.Abs(b.X - a.X) > 0.01f && MathF.Abs(b.Y - a.Y) > 0.01f)
            {
                result.Add(new SKPoint(a.X, b.Y));
            }

            result.Add(b);
        }

        return result;
    }

    /// <summary>Same idea as <see cref="AddRoundedPolygon"/> (inset each vertex along its
    /// adjacent edges and join with a curve) but for an OPEN polyline — the first/last points
    /// stay sharp corners (there's no "previous"/"next" edge to round there), only interior bends
    /// get rounded. Used for the "Rounded" elbow connector style.</summary>
    private static void AddRoundedOpenPolyline(SKPath path, float radius, IReadOnlyList<SKPoint> points, bool close)
    {
        if (points.Count < 2)
        {
            if (points.Count == 1)
            {
                path.MoveTo(points[0]);
            }

            return;
        }

        path.MoveTo(points[0]);

        for (var i = 1; i < points.Count - 1; i++)
        {
            var prev = points[i - 1];
            var curr = points[i];
            var next = points[i + 1];

            var r = Math.Min(radius, Math.Min(Distance(curr, prev), Distance(curr, next)) / 2f);
            var toPrevStart = PointAtDistance(curr, DirectionTo(curr, prev), r);
            var toNextEnd = PointAtDistance(curr, DirectionTo(curr, next), r);

            path.LineTo(toPrevStart);
            path.QuadTo(curr, toNextEnd);
        }

        path.LineTo(points[^1]);

        if (close)
        {
            path.Close();
        }
    }

    /// <summary>Draws a V-shaped arrowhead at <paramref name="to"/>, pointing away from
    /// <paramref name="from"/> — the real endpoint direction vector, generalizing the old
    /// hardcoded-angle wings so arrowheads stay correct as the user drags an endpoint anywhere.</summary>
    private static void AddArrowhead(SKPath path, SKPoint from, SKPoint to)
    {
        const float headLength = 7f;
        const float headAngle = 25f * MathF.PI / 180f;

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.001f)
        {
            return;
        }

        var ux = dx / len;
        var uy = dy / len;
        var cos = MathF.Cos(headAngle);
        var sin = MathF.Sin(headAngle);

        var wing1 = new SKPoint(
            to.X - (ux * cos - uy * sin) * headLength,
            to.Y - (uy * cos + ux * sin) * headLength);
        var wing2 = new SKPoint(
            to.X - (ux * cos + uy * sin) * headLength,
            to.Y - (uy * cos - ux * sin) * headLength);

        path.MoveTo(wing1);
        path.LineTo(to);
        path.LineTo(wing2);
    }

    /// <summary>Fits a smooth curve through every point via per-segment Catmull-Rom-to-cubic-Bezier
    /// conversion — chosen over storing explicit per-segment bezier handles so Polyline and Curve
    /// share the identical point list/interaction model; only this formula differs.</summary>
    private static void AddCatmullRomCurve(SKPath path, IReadOnlyList<SKPoint> pts, bool close = false)
    {
        if (pts.Count < 2)
        {
            return;
        }

        path.MoveTo(pts[0]);

        if (pts.Count == 2)
        {
            path.LineTo(pts[1]);

            if (close)
            {
                path.Close();
            }

            return;
        }

        for (var i = 0; i < pts.Count - 1; i++)
        {
            var p0 = i == 0 ? pts[i] : pts[i - 1];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = i + 2 < pts.Count ? pts[i + 2] : pts[i + 1];

            var c1 = new SKPoint(p1.X + (p2.X - p0.X) / 6f, p1.Y + (p2.Y - p0.Y) / 6f);
            var c2 = new SKPoint(p2.X - (p3.X - p1.X) / 6f, p2.Y - (p3.Y - p1.Y) / 6f);

            path.CubicTo(c1, c2, p2);
        }

        if (close)
        {
            path.Close();
        }
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

    /// <summary>Rebuilds a closed polygon with each corner replaced by a short quadratic-bezier
    /// curve, inset from the vertex by <paramref name="radius"/> along both adjacent edges
    /// (clamped per-vertex so it never exceeds half the shorter adjacent edge, avoiding
    /// self-intersecting corners on small/thin shapes).</summary>
    private static void AddRoundedPolygon(SKPath path, float radius, IReadOnlyList<SKPoint> points)
    {
        var n = points.Count;
        if (n < 3)
        {
            return;
        }

        for (var i = 0; i < n; i++)
        {
            var prev = points[(i - 1 + n) % n];
            var curr = points[i];
            var next = points[(i + 1) % n];

            var r = Math.Min(radius, Math.Min(Distance(curr, prev), Distance(curr, next)) / 2f);

            var toPrevStart = PointAtDistance(curr, DirectionTo(curr, prev), r);
            var toNextEnd = PointAtDistance(curr, DirectionTo(curr, next), r);

            if (i == 0)
            {
                path.MoveTo(toPrevStart);
            }
            else
            {
                path.LineTo(toPrevStart);
            }

            path.QuadTo(curr, toNextEnd);
        }

        path.Close();
    }

    private static float Distance(SKPoint a, SKPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static SKPoint DirectionTo(SKPoint from, SKPoint to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        return len > 0 ? new SKPoint(dx / len, dy / len) : new SKPoint(0, 0);
    }

    private static SKPoint PointAtDistance(SKPoint from, SKPoint direction, float distance) =>
        new(from.X + direction.X * distance, from.Y + direction.Y * distance);
}
