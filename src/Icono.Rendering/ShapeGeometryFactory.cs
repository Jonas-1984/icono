using System.Text.RegularExpressions;
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
        ShapeKind.StarCheck, ShapeKind.StarPlus, ShapeKind.StarMinus,
        ShapeKind.SquareArrowOutUpRight, ShapeKind.Blocks, ShapeKind.SwatchBook,
        ShapeKind.TriangleAlert, ShapeKind.SquareDashedBottom, ShapeKind.Share, ShapeKind.ShieldCog,
        ShapeKind.TrafficCone,

        // Office furniture, top-view floor-plan symbols — drawn as thin line-art like an
        // architectural block, not solid-filled.
        ShapeKind.Worktable, ShapeKind.RoundTable, ShapeKind.OfficeChair, ShapeKind.Monitor,
        ShapeKind.Sofa, ShapeKind.Bookshelf, ShapeKind.Door, ShapeKind.Plant, ShapeKind.Workstation,
        ShapeKind.FlowerCluster, ShapeKind.StarFlower, ShapeKind.DaisyFlower, ShapeKind.Workplace,
        ShapeKind.FilingCabinet, ShapeKind.Printer, ShapeKind.TrashBin, ShapeKind.Whiteboard,
        ShapeKind.Window, ShapeKind.Stairs, ShapeKind.PottedTree, ShapeKind.Rug, ShapeKind.Bed,
        ShapeKind.Lamp,
    ];

    /// <summary>Kinds whose local geometry is closed straight-edge polygons — the only shapes
    /// <see cref="CornerRadius"/> rounding can sensibly apply to (excludes already-curved kinds
    /// like Ellipse/Ring/HalfCircle/Cloud/Gear and the open stroke-only line-family kinds).</summary>
    private static readonly HashSet<ShapeKind> CornerRoundableKinds =
    [
        ShapeKind.Rectangle, ShapeKind.RoundedRectangle, ShapeKind.Triangle, ShapeKind.Diamond,
        ShapeKind.Pentagon, ShapeKind.Hexagon, ShapeKind.Star, ShapeKind.Parallelogram,
        ShapeKind.Trapezoid, ShapeKind.Cross, ShapeKind.SpeechBubble, ShapeKind.Text,
        ShapeKind.Octagon, ShapeKind.Heart, ShapeKind.Bookmark,
        ShapeKind.ArrowRight, ShapeKind.ArrowLeft, ShapeKind.ArrowUp, ShapeKind.ArrowDown,
        ShapeKind.ChevronRight, ShapeKind.ChevronLeft, ShapeKind.ChevronUp, ShapeKind.ChevronDown,
        ShapeKind.ArrowLeftRight, ShapeKind.ArrowUpDown, ShapeKind.CompassArrows,
        ShapeKind.NotchedArrowRight, ShapeKind.PlayTriangleRight,
    ];

    public static bool IsStrokeOnly(ShapeKind kind) => StrokeOnlyKinds.Contains(kind);

    /// <summary>Filled kinds whose local template mixes a closed fillable body with a decorative
    /// OPEN detail line — Club's stem, Pyramid's ridge, Spade's stem, Umbrella's handle/nub,
    /// Cloudy's accent arc. <see cref="Build"/>'s returned path keeps that open contour (the border
    /// stroke needs it to draw the detail line), but filling it would auto-close it into a blob the
    /// source icon never intended — see the traffic-cone/umbrella screenshot bug report. ShapeRenderer
    /// fills only the closed contours (<see cref="ExtractClosedContoursOnly"/>) for these kinds;
    /// every other (single-contour) filled kind fills the whole path exactly as before.</summary>
    private static readonly HashSet<ShapeKind> MixedFillKinds =
    [
        ShapeKind.Club, ShapeKind.Pyramid, ShapeKind.Spade, ShapeKind.Umbrella, ShapeKind.Cloudy,
    ];

    public static bool HasMixedOpenAndClosedContours(ShapeKind kind) => MixedFillKinds.Contains(kind);

    /// <summary>Keeps only the geometrically closed contours of an already-built path — closed
    /// meaning an explicit "Z"/"z" or the contour's last point landing back on its first (Skia
    /// doesn't always emit an explicit Z for a contour built via AddOval/AddPolygon even though
    /// it's a real closed loop). Never mutates <paramref name="path"/>; the caller disposes the
    /// result.</summary>
    public static SKPath ExtractClosedContoursOnly(SKPath path)
    {
        var result = new SKPath { FillType = path.FillType };

        foreach (Match m in Regex.Matches(path.ToSvgPathData(), "M[^M]*"))
        {
            var piece = m.Value;
            if (!IsClosedContour(piece))
            {
                continue;
            }

            using var sub = SKPath.ParseSvgPathData(piece);
            if (sub is not null)
            {
                result.AddPath(sub);
            }
        }

        return result;
    }

    private static bool IsClosedContour(string svgPiece)
    {
        if (svgPiece.Contains('Z') || svgPiece.Contains('z'))
        {
            return true;
        }

        using var sub = SKPath.ParseSvgPathData(svgPiece);
        if (sub is null || sub.PointCount < 2)
        {
            return false;
        }

        var first = sub.Points[0];
        var last = sub.Points[^1];
        return MathF.Abs(first.X - last.X) < 0.05f && MathF.Abs(first.Y - last.Y) < 0.05f;
    }

    /// <summary>Kinds that can never sensibly become a freeform points-based shape via "Edit
    /// Points": the line-family kinds are already Points-based (nothing to convert), a bitmap/typed
    /// text/3D box has no meaningful vertex outline, and a CombinedPath's contour count varies per
    /// INSTANCE (it isn't derivable from the ShapeKind alone the way every other kind's fixed
    /// template is).</summary>
    private static readonly HashSet<ShapeKind> NeverPointEditableKinds =
    [
        ShapeKind.Line, ShapeKind.Arrow, ShapeKind.DoubleArrow, ShapeKind.Polyline, ShapeKind.Curve,
        ShapeKind.Text, ShapeKind.Box3D, ShapeKind.RasterImage, ShapeKind.CombinedPath,
    ];

    /// <summary>Which fixed-geometry kinds can be converted into a freeform points-based shape via
    /// "Edit Points". Unlike <see cref="CornerRoundableKinds"/> (straight-edge polygons only —
    /// rounding a CURVE's already-curved vertices wouldn't mean anything), this covers EVERY kind
    /// whose local template is a single closed contour, straight-edged or not: a circle, a cloud, a
    /// gear tooth outline are all just as vertex-editable as a rectangle once "converted", they'll
    /// just start with more/curved-looking points. A MULTI-contour kind (Ring's hole, Traffic
    /// Cone's 4 pieces, Club's stem, ...) is excluded — <see cref="Shape.Points"/> is one continuous
    /// polyline/curve with no concept of separate sub-contours, so converting one would either lose
    /// pieces or wrongly connect them with a stray line.</summary>
    public static bool SupportsPointEditing(ShapeKind kind)
    {
        if (NeverPointEditableKinds.Contains(kind))
        {
            return false;
        }

        using var local = BuildLocal(kind);
        return CountContours(local) == 1;
    }

    private static int CountContours(SKPath path) => Regex.Matches(path.ToSvgPathData(), "M[^M]*").Count;

    /// <summary>Curated, hand-defined REAL geometric sub-regions for a shape kind whose visual
    /// "bands" aren't separate SVG subpaths at all — e.g. Traffic Cone's rings are just thin lines
    /// drawn ACROSS one continuous cone silhouette, so <see cref="ExtractClosedContoursOnly"/>
    /// (which only separates already-closed vs. open subpaths) can't produce cap/band/base pieces
    /// from it. This slices the silhouette by the rings' own Y positions using EXACT analytical
    /// linear interpolation along the cone's straight side-walls, then re-embeds each curved edge
    /// (the apex rounding, the base arc) using its ORIGINAL, UNMODIFIED SVG arc command — not
    /// SkiaSharp's boolean SKPath.Op (tried first; its result is always a flattened polygon
    /// approximation of any curve involved, however fine the tessellation, which visibly faceted
    /// the cone's rounded apex/base once rendered at real canvas size). Null for any kind with no
    /// curated regions.</summary>
    public static IReadOnlyList<string>? GetColorableRegions(ShapeKind kind)
    {
        if (kind != ShapeKind.TrafficCone)
        {
            return null;
        }

        // Read directly off the cone body's own path — "M16.949 14.14a5 2.5 0 1 1-9.9 0L10.063
        // 3.5a2 2 0 0 1 3.874 0z" — bottom-right (arc start) -> bottom-left (arc end) -> [straight]
        // apex-left -> [arc] apex-right -> [straight, via Z] back to bottom-right.
        const double bottomRightX = 16.949, bottomLeftX = 7.049, bottomY = 14.14;
        const double apexLeftX = 10.063, apexRightX = 13.937, apexY = 3.5;
        const double ring1Y = 10.966; // "M16.05 10.966..." — the lower ring
        const double ring2Y = 6.57; // "M9.194 6.57..." — the upper ring

        // Exact intersection of each straight side-wall with a horizontal cut at targetY.
        double LeftSideXAt(double targetY) =>
            bottomLeftX + (apexLeftX - bottomLeftX) * (bottomY - targetY) / (bottomY - apexY);
        double RightSideXAt(double targetY) =>
            apexRightX + (bottomRightX - apexRightX) * (targetY - apexY) / (bottomY - apexY);

        var leftAtRing2 = LeftSideXAt(ring2Y);
        var rightAtRing2 = RightSideXAt(ring2Y);
        var leftAtRing1 = LeftSideXAt(ring1Y);
        var rightAtRing1 = RightSideXAt(ring1Y);

        // Cap: the true apex arc, unmodified, plus straight cuts down to ring2Y on both sides.
        var cap = FormattableString.Invariant(
            $"M{leftAtRing2},{ring2Y} L{apexLeftX},{apexY} a2 2 0 0 1 3.874 0 L{rightAtRing2},{ring2Y} Z");

        // Band1: purely straight-walled in this Y range (no curve up here) — a simple quadrilateral.
        var band1 = FormattableString.Invariant(
            $"M{leftAtRing2},{ring2Y} L{rightAtRing2},{ring2Y} L{rightAtRing1},{ring1Y} L{leftAtRing1},{ring1Y} Z");

        // Band2: straight cut across the top at ring1Y, then the true base arc, unmodified.
        var band2 = FormattableString.Invariant(
            $"M{leftAtRing1},{ring1Y} L{rightAtRing1},{ring1Y} L{bottomRightX},{bottomY} a5 2.5 0 1 1 -9.9 0 Z");

        const string basePlate =
            "M16.923,14.049 l4.48,2.04 a1 1 0 0 1 .001 1.831 l-8.574 3.9 a2 2 0 0 1-1.66 0 " +
            "l-8.574-3.91 a1 1 0 0 1 0-1.83 l4.484-2.04";

        return [cap, band1, band2, basePlate];
    }

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
            var pointsPath = BuildFromPoints(
                shape.Kind, points, shape.X, shape.Y, shape.PathRoutingStyle, shape.CornerRadius,
                shape.LineStartHead, shape.LineEndHead);

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
        // given CornerRadius reads the same regardless of the shape's aspect ratio. RasterImage is
        // checked separately from CornerRoundableKinds (rather than added to that set) so it stays
        // roundable without also opting into "Edit Points" (excluded there via
        // NeverPointEditableKinds — a freeform vertex list makes no sense for a bitmap).
        if (shape.CornerRadius > 0 && (CornerRoundableKinds.Contains(shape.Kind) || shape.Kind == ShapeKind.RasterImage))
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
            case ShapeKind.RasterImage:
                // Full-bleed, unlike the inset Rectangle template — the imported bitmap should
                // fill exactly the shape's X/Y/Width/Height with no gap (ShapeRenderer draws the
                // actual pixels; this geometry only drives hit-testing/handles/selection outline).
                path.AddRect(SKRect.Create(0, 0, 24, 24));
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
            {
                using var shieldPath = SKPath.ParseSvgPathData(
                    "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z");
                if (shieldPath is not null)
                {
                    path.AddPath(shieldPath);
                }

                break;
            }
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

            // The following icon glyph kinds are parsed straight from the user-supplied Lucide
            // SVGs (same approach as Club above), each source icon's separate <path>/<rect>
            // elements concatenated into one SVG path-data string. Where a source subpath used a
            // relative "m" moveto, it's rewritten to an absolute "M" (keeping the following
            // implicit points as explicit relative "l" segments) since only the very first moveto
            // of a concatenated string is implicitly absolute — everything after it stays exactly
            // as authored.
            case ShapeKind.StarCheck:
            {
                using var starCheckPath = SKPath.ParseSvgPathData(
                    "m19.06 12.501 2.78-2.707a.53.53 0 0 0-.294-.905l-5.166-.755a2.1 2.1 0 0 1-1.595-1.16l-2.31-4.68a.53.53 0 0 0-.95.001L9.216 6.974a2.1 2.1 0 0 1-1.597 1.16l-5.165.755a.53.53 0 0 0-.294.906l3.736 3.637a2.1 2.1 0 0 1 .611 1.879l-.88 5.139a.53.53 0 0 0 .769.56l4.617-2.428.027-.014 M15,18 l2,2 l4,-4");
                if (starCheckPath is not null)
                {
                    path.AddPath(starCheckPath);
                }

                break;
            }
            case ShapeKind.StarPlus:
            {
                using var starPlusPath = SKPath.ParseSvgPathData(
                    "M11.013 18.582 6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.12 2.12 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.12 2.12 0 0 0 1.597-1.16l2.309-4.679a.53.53 0 0 1 .95 0l2.31 4.679a2.12 2.12 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904L20 11.5 M15 18h6 M18 15v6");
                if (starPlusPath is not null)
                {
                    path.AddPath(starPlusPath);
                }

                break;
            }
            case ShapeKind.StarMinus:
            {
                using var starMinusPath = SKPath.ParseSvgPathData(
                    "M15 18h6 M17.688 14a2.1 2.1 0 0 1 .416-.568l3.736-3.638a.53.53 0 0 0-.294-.905l-5.166-.755a2.1 2.1 0 0 1-1.595-1.16l-2.31-4.68a.53.53 0 0 0-.95.001L9.216 6.974a2.1 2.1 0 0 1-1.597 1.16l-5.165.755a.53.53 0 0 0-.294.906l3.736 3.637a2.1 2.1 0 0 1 .611 1.879l-.88 5.139a.53.53 0 0 0 .769.56l4.617-2.428.027-.014");
                if (starMinusPath is not null)
                {
                    path.AddPath(starMinusPath);
                }

                break;
            }
            case ShapeKind.SquareArrowOutUpRight:
            {
                using var squareArrowPath = SKPath.ParseSvgPathData(
                    "M21 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h6 M21,3 l-9,9 M15 3h6v6");
                if (squareArrowPath is not null)
                {
                    path.AddPath(squareArrowPath);
                }

                break;
            }
            case ShapeKind.Blocks:
            {
                using var blocksPath = SKPath.ParseSvgPathData(
                    "M10 22V7a1 1 0 0 0-1-1H4a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-5a1 1 0 0 0-1-1H2 M15,2 H21 A1,1 0 0 1 22,3 V9 A1,1 0 0 1 21,10 H15 A1,1 0 0 1 14,9 V3 A1,1 0 0 1 15,2 Z");
                if (blocksPath is not null)
                {
                    path.AddPath(blocksPath);
                }

                break;
            }
            case ShapeKind.SwatchBook:
            {
                using var swatchBookPath = SKPath.ParseSvgPathData(
                    "M11 17a4 4 0 0 1-8 0V5a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2Z M16.7 13H19a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2H7 M7 17h.01 M11,8 l2.3,-2.3 a2.4 2.4 0 0 1 3.404 .004 L18.6 7.6 a2.4 2.4 0 0 1 .026 3.434 L9.9 19.8");
                if (swatchBookPath is not null)
                {
                    path.AddPath(swatchBookPath);
                }

                break;
            }
            case ShapeKind.LensConcave:
            {
                using var lensConcavePath = SKPath.ParseSvgPathData(
                    "M7 2a1 1 0 0 0-.8 1.6 14 14 0 0 1 0 16.8A1 1 0 0 0 7 22h10a1 1 0 0 0 .8-1.6 14 14 0 0 1 0-16.8A1 1 0 0 0 17 2z");
                if (lensConcavePath is not null)
                {
                    path.AddPath(lensConcavePath);
                }

                break;
            }

            case ShapeKind.Cylinder:
                // Filled ellipse (rim) unioned with an open body outline whose top edge is
                // implicitly closed by the fill (straight line back to the start point) — same
                // "parsed straight from the source SVG" approach as Club/Shield above. The oval
                // MUST be added CounterClockwise to match the body path's own winding direction —
                // Skia's default nonzero fill rule CANCELS overlapping regions that wind in
                // OPPOSITE directions instead of just unioning them, which left a transparent gap
                // straight through the rim (confirmed empirically: default direction leaves the
                // whole lower half of the rim ellipse unfilled where it overlaps the body).
                path.AddOval(SKRect.Create(3, 2, 18, 6), SKPathDirection.CounterClockwise);
                using (var cylinderBody = SKPath.ParseSvgPathData("M3 5v14a9 3 0 0 0 18 0V5"))
                {
                    if (cylinderBody is not null)
                    {
                        path.AddPath(cylinderBody);
                    }
                }

                break;
            case ShapeKind.Cone:
                using (var coneSides = SKPath.ParseSvgPathData("m20.9 18.55-8-15.98a1 1 0 0 0-1.8 0l-8 15.98"))
                {
                    if (coneSides is not null)
                    {
                        path.AddPath(coneSides);
                    }
                }

                // Same CounterClockwise fix as Cylinder above — the default direction canceled out
                // against the sides path's winding and left a gap through the base ellipse.
                path.AddOval(SKRect.Create(3, 16, 18, 6), SKPathDirection.CounterClockwise);
                break;
            case ShapeKind.Pyramid:
            {
                using var pyramidPath = SKPath.ParseSvgPathData(
                    "M2.5 16.88a1 1 0 0 1-.32-1.43l9-13.02a1 1 0 0 1 1.64 0l9 13.01a1 1 0 0 1-.32 1.44l-8.51 4.86a2 2 0 0 1-1.98 0Z M12 2v20");
                if (pyramidPath is not null)
                {
                    path.AddPath(pyramidPath);
                }

                break;
            }
            case ShapeKind.TriangleAlert:
            {
                using var alertPath = SKPath.ParseSvgPathData(
                    "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3 M12 9v4 M12 17h.01");
                if (alertPath is not null)
                {
                    path.AddPath(alertPath);
                }

                break;
            }
            case ShapeKind.Spade:
            {
                using var spadePath = SKPath.ParseSvgPathData(
                    "M2 14.499a5.5 5.5 0 0 0 9.591 3.675.6.6 0 0 1 .818.001A5.5 5.5 0 0 0 22 14.5c0-2.29-1.5-4-3-5.5l-5.492-5.312a2 2 0 0 0-3-.02L5 8.999c-1.5 1.5-3 3.2-3 5.5 M12 18v4");
                if (spadePath is not null)
                {
                    path.AddPath(spadePath);
                }

                break;
            }
            case ShapeKind.SquareDashedBottom:
            {
                using var dashedPath = SKPath.ParseSvgPathData(
                    "M5 21a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2 M9 21h1 M14 21h1");
                if (dashedPath is not null)
                {
                    path.AddPath(dashedPath);
                }

                break;
            }
            case ShapeKind.Share:
            {
                using var sharePath = SKPath.ParseSvgPathData(
                    "M12 2v13 M16,6 l-4,-4 l-4,4 M4 12v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8");
                if (sharePath is not null)
                {
                    path.AddPath(sharePath);
                }

                break;
            }
            case ShapeKind.ShieldCog:
                using (var shieldCogPath = SKPath.ParseSvgPathData(
                           "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z M10.929,14.467 l-0.383,0.924 M10.929 8.923 10.546 8 M13.225 8.923 13.608 8 M13.607,15.391 l-0.382,-0.924 M14.849,10.547 l0.923,-0.383 M14.849,12.843 l0.923,0.383 M9.305,10.547 l-0.923,-0.383 M9.305,12.843 l-0.923,0.383"))
                {
                    if (shieldCogPath is not null)
                    {
                        path.AddPath(shieldCogPath);
                    }
                }

                path.AddOval(SKRect.Create(9.077f, 8.695f, 6, 6));
                break;
            case ShapeKind.Umbrella:
            {
                using var umbrellaPath = SKPath.ParseSvgPathData(
                    "M20.992 13a1 1 0 0 0 .97-1.274 10.284 10.284 0 0 0-19.923 0A1 1 0 0 0 3 13z M12 13v7a2 2 0 0 0 4 0 M12 2v2");
                if (umbrellaPath is not null)
                {
                    path.AddPath(umbrellaPath);
                }

                break;
            }
            case ShapeKind.Cloudy:
            {
                using var cloudyPath = SKPath.ParseSvgPathData(
                    "M17.5 12a1 1 0 1 1 0 9H9.006a7 7 0 1 1 6.702-9z M21.832 9A3 3 0 0 0 19 7h-2.207a5.5 5.5 0 0 0-10.72.61");
                if (cloudyPath is not null)
                {
                    path.AddPath(cloudyPath);
                }

                break;
            }
            case ShapeKind.TrafficCone:
            {
                using var trafficConePath = SKPath.ParseSvgPathData(
                    "M16.949 14.14a5 2.5 0 1 1-9.9 0L10.063 3.5a2 2 0 0 1 3.874 0z M16.05 10.966a5 2.5 0 0 1-8.1 0 " +
                    "M9.194 6.57a5 2.5 0 0 0 5.61 0 M16.923,14.049 l4.48,2.04 a1 1 0 0 1 .001 1.831 l-8.574 3.9 " +
                    "a2 2 0 0 1-1.66 0 l-8.574-3.91 a1 1 0 0 1 0-1.83 l4.484-2.04");
                if (trafficConePath is not null)
                {
                    path.AddPath(trafficConePath);
                }

                break;
            }

            case ShapeKind.Worktable:
            {
                // A plain desk rectangle with a keyboard-tray notch cut into the front edge — the
                // detail that reads as "worktable" rather than the generic Rectangle shape.
                using var worktablePath = SKPath.ParseSvgPathData(
                    "M2,4 L22,4 L22,20 L15,20 L15,17 L9,17 L9,20 L2,20 Z");
                if (worktablePath is not null)
                {
                    path.AddPath(worktablePath);
                }

                break;
            }

            case ShapeKind.RoundTable:
            {
                // Two concentric circles — the outer tabletop edge plus an inner rim line.
                using var roundTablePath = SKPath.ParseSvgPathData(
                    "M3,12 A9,9 0 1 0 21,12 A9,9 0 1 0 3,12 Z " +
                    "M6.5,12 A5.5,5.5 0 1 0 17.5,12 A5.5,5.5 0 1 0 6.5,12 Z");
                if (roundTablePath is not null)
                {
                    path.AddPath(roundTablePath);
                }

                break;
            }

            case ShapeKind.OfficeChair:
            {
                // Oval seat + a curved backrest above it + a short post/base line below — the
                // standard minimal "office chair, top view" floor-plan symbol.
                using var officeChairPath = SKPath.ParseSvgPathData(
                    "M6,13 A6,5 0 1 0 18,13 A6,5 0 1 0 6,13 Z M7,7 A5,3 0 0 1 17,7 M12,18 L12,21");
                if (officeChairPath is not null)
                {
                    path.AddPath(officeChairPath);
                }

                break;
            }

            case ShapeKind.Monitor:
            {
                // Top view of a monitor sitting on a desk: the screen's thin top edge plus the
                // stand base behind it.
                using var monitorPath = SKPath.ParseSvgPathData(
                    "M4,7 L20,7 L20,10 L4,10 Z M9,13 L15,13 L15,19 L9,19 Z M12,10 L12,13");
                if (monitorPath is not null)
                {
                    path.AddPath(monitorPath);
                }

                break;
            }

            case ShapeKind.Sofa:
            {
                // Main seat cushion rectangle flanked by two taller armrest blocks, plus a back
                // cushion line — the classic overlapping-rectangles "sofa, top view" convention.
                using var sofaPath = SKPath.ParseSvgPathData(
                    "M3,9 L21,9 L21,18 L3,18 Z M3,6 L7,6 L7,20 L3,20 Z M17,6 L21,6 L21,20 L17,20 Z M8,11 L16,11");
                if (sofaPath is not null)
                {
                    path.AddPath(sofaPath);
                }

                break;
            }

            case ShapeKind.Bookshelf:
            {
                using var bookshelfPath = SKPath.ParseSvgPathData(
                    "M3,4 L21,4 L21,20 L3,20 Z M9,4 L9,20 M15,4 L15,20");
                if (bookshelfPath is not null)
                {
                    path.AddPath(bookshelfPath);
                }

                break;
            }

            case ShapeKind.Door:
            {
                // Standard architectural door symbol: the wall opening, the door panel shown
                // open, and the quarter-circle swing arc between them.
                using var doorPath = SKPath.ParseSvgPathData("M4,20 L20,20 M4,20 L4,6 M4,6 A14,14 0 0 1 18,20");
                if (doorPath is not null)
                {
                    path.AddPath(doorPath);
                }

                break;
            }

            case ShapeKind.Plant:
            {
                // A round pot with a few radiating "leaf" lines above it.
                using var plantPath = SKPath.ParseSvgPathData(
                    "M7,15 A5,5 0 1 0 17,15 A5,5 0 1 0 7,15 Z " +
                    "M12,14 L12,4 M12,14 L6,7 M12,14 L18,7 M12,14 L5,13 M12,14 L19,13");
                if (plantPath is not null)
                {
                    path.AddPath(plantPath);
                }

                break;
            }

            case ShapeKind.Workstation:
            {
                // A desk surface with a laptop, keyboard, notebook, and mouse laid out on it —
                // the "occupied desk" companion to the plain Worktable symbol.
                using var workstationPath = SKPath.ParseSvgPathData(
                    "M2,3 L22,3 L22,21 L2,21 Z " +
                    "M4,5 L10,5 L10,10 L4,10 Z M7,5 L7,10 " +
                    "M12,6 L20,6 L20,10 L12,10 Z M14,7.3 L14,8.7 M16,7.3 L16,8.7 M18,7.3 L18,8.7 " +
                    "M5,13 L11,13 L11,16.5 L5,16.5 Z M4,16.5 L12,16.5 L12,19 L4,19 Z " +
                    "M15,16.5 A2,2.5 0 1 0 19,16.5 A2,2.5 0 1 0 15,16.5 Z");
                if (workstationPath is not null)
                {
                    path.AddPath(workstationPath);
                }

                break;
            }

            case ShapeKind.FlowerCluster:
            {
                // A cluster of small round petals around a center, plus a few scattered dots
                // trailing off below-right — a hydrangea/lace-cap-style flower, top view.
                using var flowerClusterPath = SKPath.ParseSvgPathData(
                    "M14.2,11 A2.3,2.3 0 1 0 18.8,11 A2.3,2.3 0 1 0 14.2,11 Z " +
                    "M11.95,14.9 A2.3,2.3 0 1 0 16.55,14.9 A2.3,2.3 0 1 0 11.95,14.9 Z " +
                    "M7.45,14.9 A2.3,2.3 0 1 0 12.05,14.9 A2.3,2.3 0 1 0 7.45,14.9 Z " +
                    "M5.2,11 A2.3,2.3 0 1 0 9.8,11 A2.3,2.3 0 1 0 5.2,11 Z " +
                    "M7.45,7.1 A2.3,2.3 0 1 0 12.05,7.1 A2.3,2.3 0 1 0 7.45,7.1 Z " +
                    "M11.95,7.1 A2.3,2.3 0 1 0 16.55,7.1 A2.3,2.3 0 1 0 11.95,7.1 Z " +
                    "M10.5,11 A1.5,1.5 0 1 0 13.5,11 A1.5,1.5 0 1 0 10.5,11 Z " +
                    "M15.5,17 A0.5,0.5 0 1 0 16.5,17 A0.5,0.5 0 1 0 15.5,17 Z " +
                    "M17.5,19 A0.5,0.5 0 1 0 18.5,19 A0.5,0.5 0 1 0 17.5,19 Z " +
                    "M13.5,20 A0.5,0.5 0 1 0 14.5,20 A0.5,0.5 0 1 0 13.5,20 Z");
                if (flowerClusterPath is not null)
                {
                    path.AddPath(flowerClusterPath);
                }

                break;
            }

            case ShapeKind.StarFlower:
            {
                // Six bold pointed petals radiating from a textured center — an echinacea/aster
                // style flower, top view.
                using var starFlowerPath = SKPath.ParseSvgPathData(
                    "M12,12.6 L21,11 L12,9.4 Z " +
                    "M10.61,11.8 L16.5,18.79 L13.39,10.2 Z " +
                    "M10.61,10.2 L7.5,18.79 L13.39,11.8 Z " +
                    "M12,9.4 L3,11 L12,12.6 Z " +
                    "M13.39,10.2 L7.5,3.21 L10.61,11.8 Z " +
                    "M13.39,11.8 L16.5,3.21 L10.61,10.2 Z " +
                    "M10,11 A2,2 0 1 0 14,11 A2,2 0 1 0 10,11 Z " +
                    "M11.3,10.3 A0.4,0.4 0 1 0 12.1,10.3 A0.4,0.4 0 1 0 11.3,10.3 Z");
                if (starFlowerPath is not null)
                {
                    path.AddPath(starFlowerPath);
                }

                break;
            }

            case ShapeKind.DaisyFlower:
            {
                // Eight thin, elongated petals radiating from a small plain center — a classic
                // daisy, top view.
                using var daisyFlowerPath = SKPath.ParseSvgPathData(
                    "M12,12.7 L21,12 L12,11.3 Z " +
                    "M11.5,12.5 L18.36,18.36 L12.5,11.5 Z " +
                    "M11.3,12 L12,21 L12.7,12 Z " +
                    "M11.5,11.5 L5.64,18.36 L12.5,12.5 Z " +
                    "M12,11.3 L3,12 L12,12.7 Z " +
                    "M12.5,11.5 L5.64,5.64 L11.5,12.5 Z " +
                    "M12.7,12 L12,3 L11.3,12 Z " +
                    "M12.5,12.5 L18.36,5.64 L11.5,11.5 Z " +
                    "M10.7,12 A1.3,1.3 0 1 0 13.3,12 A1.3,1.3 0 1 0 10.7,12 Z");
                if (daisyFlowerPath is not null)
                {
                    path.AddPath(daisyFlowerPath);
                }

                break;
            }

            case ShapeKind.Workplace:
            {
                // A desk with its 4 legs poking out at the corners (the classic top-view
                // floor-plan convention), a monitor on a stand, a keyboard with a dot grid, a
                // mouse, and a small dial/knob accessory in the corner.
                using var workplacePath = SKPath.ParseSvgPathData(
                    "M3,5 L21,5 L21,19 L3,19 Z " +
                    "M3,5 L1.5,3.5 M21,5 L22.5,3.5 M3,19 L1.5,20.5 M21,19 L22.5,20.5 " +
                    "M8,7 L16,7 L16,11 L8,11 Z M11,11 L13,11 L13,12.5 L11,12.5 Z M10,12.5 L14,12.5 L14,13.3 L10,13.3 Z " +
                    "M6,14.3 L15,14.3 L15,17.5 L6,17.5 Z " +
                    "M7.65,15.3 A0.35,0.35 0 1 0 8.35,15.3 A0.35,0.35 0 1 0 7.65,15.3 Z " +
                    "M10.15,15.3 A0.35,0.35 0 1 0 10.85,15.3 A0.35,0.35 0 1 0 10.15,15.3 Z " +
                    "M12.65,15.3 A0.35,0.35 0 1 0 13.35,15.3 A0.35,0.35 0 1 0 12.65,15.3 Z " +
                    "M7.65,16.7 A0.35,0.35 0 1 0 8.35,16.7 A0.35,0.35 0 1 0 7.65,16.7 Z " +
                    "M10.15,16.7 A0.35,0.35 0 1 0 10.85,16.7 A0.35,0.35 0 1 0 10.15,16.7 Z " +
                    "M12.65,16.7 A0.35,0.35 0 1 0 13.35,16.7 A0.35,0.35 0 1 0 12.65,16.7 Z " +
                    "M16.1,16.5 A1.4,1.8 0 1 0 18.9,16.5 A1.4,1.8 0 1 0 16.1,16.5 Z " +
                    "M16.4,8.5 A1.6,1.6 0 1 0 19.6,8.5 A1.6,1.6 0 1 0 16.4,8.5 Z M18,8.5 L18,7");
                if (workplacePath is not null)
                {
                    path.AddPath(workplacePath);
                }

                break;
            }

            case ShapeKind.FilingCabinet:
            {
                using var filingCabinetPath = SKPath.ParseSvgPathData(
                    "M3,3 L21,3 L21,21 L3,21 Z M3,9 L21,9 M3,15 L21,15 M11,6 L13,6 M11,12 L13,12 M11,18 L13,18");
                if (filingCabinetPath is not null)
                {
                    path.AddPath(filingCabinetPath);
                }

                break;
            }

            case ShapeKind.Printer:
            {
                using var printerPath = SKPath.ParseSvgPathData(
                    "M3,8 L21,8 L21,19 L3,19 Z M6,8 L6,4 L18,4 L18,8 M6,12 L14,12 " +
                    "M15.8,15 A1.2,1.2 0 1 0 18.2,15 A1.2,1.2 0 1 0 15.8,15 Z");
                if (printerPath is not null)
                {
                    path.AddPath(printerPath);
                }

                break;
            }

            case ShapeKind.TrashBin:
            {
                using var trashBinPath = SKPath.ParseSvgPathData(
                    "M4,12 A8,8 0 1 0 20,12 A8,8 0 1 0 4,12 Z M6,12 A6,6 0 1 0 18,12 A6,6 0 1 0 6,12 Z");
                if (trashBinPath is not null)
                {
                    path.AddPath(trashBinPath);
                }

                break;
            }

            case ShapeKind.Whiteboard:
            {
                using var whiteboardPath = SKPath.ParseSvgPathData(
                    "M3,9 L21,9 L21,15 L3,15 Z M4,9 L4,7 M20,9 L20,7");
                if (whiteboardPath is not null)
                {
                    path.AddPath(whiteboardPath);
                }

                break;
            }

            case ShapeKind.Window:
            {
                // Standard architectural window symbol: the wall opening with the double glazing
                // lines across it.
                using var windowPath = SKPath.ParseSvgPathData(
                    "M3,9 L21,9 M3,13 L21,13 M3,9 L3,13 M21,9 L21,13");
                if (windowPath is not null)
                {
                    path.AddPath(windowPath);
                }

                break;
            }

            case ShapeKind.Stairs:
            {
                using var stairsPath = SKPath.ParseSvgPathData(
                    "M4,4 L20,4 M4,8 L20,8 M4,12 L20,12 M4,16 L20,16 M4,20 L20,20 " +
                    "M12,20 L12,3 M9,6 L12,3 L15,6");
                if (stairsPath is not null)
                {
                    path.AddPath(stairsPath);
                }

                break;
            }

            case ShapeKind.PottedTree:
            {
                using var pottedTreePath = SKPath.ParseSvgPathData(
                    "M5,9 A7,7 0 1 0 19,9 A7,7 0 1 0 5,9 Z M9,16 L15,16 L14,21 L10,21 Z");
                if (pottedTreePath is not null)
                {
                    path.AddPath(pottedTreePath);
                }

                break;
            }

            case ShapeKind.Rug:
            {
                using var rugPath = SKPath.ParseSvgPathData(
                    "M3,4 L21,4 L21,20 L3,20 Z M5,6 L19,6 L19,18 L5,18 Z");
                if (rugPath is not null)
                {
                    path.AddPath(rugPath);
                }

                break;
            }

            case ShapeKind.Bed:
            {
                using var bedPath = SKPath.ParseSvgPathData(
                    "M4,6 L20,6 L20,21 L4,21 Z M6,7.5 L18,7.5 L18,11.5 L6,11.5 Z M4,15 L20,15");
                if (bedPath is not null)
                {
                    path.AddPath(bedPath);
                }

                break;
            }

            case ShapeKind.Lamp:
            {
                using var lampPath = SKPath.ParseSvgPathData(
                    "M5,12 A7,7 0 1 0 19,12 A7,7 0 1 0 5,12 Z " +
                    "M10.8,12 A1.2,1.2 0 1 0 13.2,12 A1.2,1.2 0 1 0 10.8,12 Z");
                if (lampPath is not null)
                {
                    path.AddPath(lampPath);
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
        PathRoutingStyle routingStyle, double cornerRadius = 0,
        ArrowHeadKind startHead = ArrowHeadKind.None, ArrowHeadKind endHead = ArrowHeadKind.None)
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

            if (pts.Length >= 2)
            {
                AddHead(path, pts[1], pts[0], startHead);
                AddHead(path, pts[^2], pts[^1], endHead);
            }

            return path;
        }

        if (effectiveStyle == PathRoutingStyle.Rounded && pts.Length >= 2)
        {
            var routed = BuildOrthogonalPoints(pts);
            var radius = (float)(cornerRadius > 0 ? cornerRadius : 8);
            AddRoundedOpenPolyline(path, radius, routed, closeForFill);

            if (routed.Count >= 2)
            {
                AddHead(path, routed[1], routed[0], startHead);
                AddHead(path, routed[^2], routed[^1], endHead);
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

        if (drawnPoints.Count >= 2)
        {
            AddHead(path, drawnPoints[1], drawnPoints[0], startHead);
            AddHead(path, drawnPoints[^2], drawnPoints[^1], endHead);
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

    /// <summary>Draws the marker for <see cref="ArrowHeadKind"/> at <paramref name="to"/>, oriented
    /// away from <paramref name="from"/> — the real endpoint direction vector, so heads stay
    /// correct as the user drags an endpoint anywhere. Wing-shaped heads (Open/Filled/Thin/Half)
    /// are left as an UNCLOSED sub-path so they only get stroked, matching the line itself; the dot
    /// markers (Circle/Diamond/Square) are closed sub-paths, which <see cref="ExtractClosedContoursOnly"/>
    /// picks up automatically — no extra plumbing needed for them to render solid-filled.</summary>
    private static void AddHead(SKPath path, SKPoint from, SKPoint to, ArrowHeadKind kind)
    {
        if (kind == ArrowHeadKind.None)
        {
            return;
        }

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.001f)
        {
            return;
        }

        var ux = dx / len;
        var uy = dy / len;
        var px = -uy;
        var py = ux;

        switch (kind)
        {
            case ArrowHeadKind.Open:
                AddWings(path, to, ux, uy, 7f, 25f, close: false, singleWingOnly: false);
                break;
            case ArrowHeadKind.Filled:
                AddWings(path, to, ux, uy, 8f, 22f, close: true, singleWingOnly: false);
                break;
            case ArrowHeadKind.Thin:
                AddWings(path, to, ux, uy, 10f, 12f, close: false, singleWingOnly: false);
                break;
            case ArrowHeadKind.Half:
                AddWings(path, to, ux, uy, 7f, 25f, close: false, singleWingOnly: true);
                break;
            case ArrowHeadKind.Circle:
            {
                const float radius = 3.2f;
                path.AddCircle(to.X - ux * radius, to.Y - uy * radius, radius);
                break;
            }
            case ArrowHeadKind.Diamond:
            {
                const float half = 3.6f;
                var cx = to.X - ux * half;
                var cy = to.Y - uy * half;
                path.MoveTo(cx + ux * half, cy + uy * half);
                path.LineTo(cx + px * half, cy + py * half);
                path.LineTo(cx - ux * half, cy - uy * half);
                path.LineTo(cx - px * half, cy - py * half);
                path.Close();
                break;
            }
            case ArrowHeadKind.Square:
            {
                const float half = 3f;
                var cx = to.X - ux * half;
                var cy = to.Y - uy * half;
                path.MoveTo(cx + ux * half + px * half, cy + uy * half + py * half);
                path.LineTo(cx + ux * half - px * half, cy + uy * half - py * half);
                path.LineTo(cx - ux * half - px * half, cy - uy * half - py * half);
                path.LineTo(cx - ux * half + px * half, cy - uy * half + py * half);
                path.Close();
                break;
            }
            case ArrowHeadKind.Bar:
                const float barHalf = 4f;
                path.MoveTo(to.X + px * barHalf, to.Y + py * barHalf);
                path.LineTo(to.X - px * barHalf, to.Y - py * barHalf);
                break;
        }
    }

    /// <summary>Shared geometry for the wing-shaped head styles — one or two lines rotated
    /// <paramref name="headAngleDegrees"/> off the shaft direction, meeting at <paramref name="to"/>.
    /// <paramref name="close"/> turns the two-wing V into a closed (fillable) triangle instead of an
    /// open chevron; <paramref name="singleWingOnly"/> draws just one wing (the "Half" style).</summary>
    private static void AddWings(
        SKPath path, SKPoint to, float ux, float uy, float headLength, float headAngleDegrees,
        bool close, bool singleWingOnly)
    {
        var headAngle = headAngleDegrees * MathF.PI / 180f;
        var cos = MathF.Cos(headAngle);
        var sin = MathF.Sin(headAngle);

        var wing1 = new SKPoint(
            to.X - (ux * cos - uy * sin) * headLength,
            to.Y - (uy * cos + ux * sin) * headLength);

        path.MoveTo(wing1);
        path.LineTo(to);

        if (!singleWingOnly)
        {
            var wing2 = new SKPoint(
                to.X - (ux * cos + uy * sin) * headLength,
                to.Y - (uy * cos - ux * sin) * headLength);
            path.LineTo(wing2);
        }

        if (close)
        {
            path.Close();
        }
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
