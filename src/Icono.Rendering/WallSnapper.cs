using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Which snapping helpers are on while a wall is drawn. Each one can be switched off by the user.</summary>
public sealed class WallSnapOptions
{
    /// <summary>Master switch for all wall snapping.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Snap to the horizontal and vertical direction from the previous point.</summary>
    public bool Axis { get; set; } = true;

    /// <summary>Snap to steps of <see cref="AngleStepDegrees"/> (15 degrees: 0, 15, 30, 45, ...).</summary>
    public bool AngleSteps { get; set; }

    public double AngleStepDegrees { get; set; } = 15;

    /// <summary>Snap to walls running parallel or at a right angle to an existing wall.</summary>
    public bool Parallel { get; set; } = true;

    /// <summary>Line up with the x or y position of other points (guide lines show it), so facing walls sit exactly opposite.</summary>
    public bool Align { get; set; } = true;

    /// <summary>Snap the length of the new segment to the length of another segment.</summary>
    public bool EqualLength { get; set; } = true;

    /// <summary>Round the length of the new segment to a multiple of <see cref="LengthStepPx"/>.</summary>
    public bool RoundLength { get; set; }

    /// <summary>Step for <see cref="RoundLength"/> in px (default 5 cm at 96 DPI).</summary>
    public double LengthStepPx { get; set; } = 5 / 2.54 * 96;

    /// <summary>Snap to the corners and ends of other walls.</summary>
    public bool Endpoints { get; set; } = true;

    /// <summary>Snap to the middle of other wall segments.</summary>
    public bool Midpoints { get; set; } = true;

    /// <summary>Snap to the nearest place on another wall's centre line.</summary>
    public bool OnWall { get; set; }
}

public enum SnapKind
{
    None,
    Corner,
    Midpoint,
    OnWall,
    Axis,
    Angle,
    Parallel,
    Align,
    EqualLength,
    RoundLength,
}

/// <summary>A helper line or mark shown while snapping.</summary>
public sealed record SnapGuide(SnapKind Kind, SKPoint From, SKPoint To);

public sealed class WallSnapResult
{
    public SKPoint Point { get; set; }

    /// <summary>True when something snapped (otherwise <see cref="Point"/> is the plain pointer position).</summary>
    public bool Snapped { get; set; }

    /// <summary>The kind of point snapped to, if any (corner, midpoint, on wall).</summary>
    public SnapKind PointKind { get; set; }

    public List<SnapGuide> Guides { get; } = [];

    /// <summary>What is being drawn: "length - angle" and what it snapped to.</summary>
    public string Label { get; set; } = string.Empty;
}

/// <summary>The snapping of the wall tool: corners, midpoints and walls, horizontal / vertical and angle steps, parallel and
/// perpendicular walls, alignment with other points, equal and rounded lengths.</summary>
public static class WallSnapper
{
    /// <param name="cursor">Pointer position in document space.</param>
    /// <param name="previous">The last placed point of the wall being drawn (null for its first point).</param>
    /// <param name="ownPoints">The placed points of the wall being drawn (including <paramref name="previous"/>).</param>
    /// <param name="tolerance">How near (in document px) something has to be to snap.</param>
    /// <param name="forceAxis">Shift held: lock to the horizontal or vertical direction whatever the distance.</param>
    public static WallSnapResult Snap(
        SKPoint cursor, SKPoint? previous, IReadOnlyList<SKPoint> ownPoints, Document document,
        WallSnapOptions options, double tolerance, bool forceAxis = false)
    {
        var result = new WallSnapResult { Point = cursor };
        var tol = (float)tolerance;

        // everything the new wall can relate to
        var visible = document.VisibleShapes().ToList();
        var walls = visible.Where(s => s.Kind == ShapeKind.Wall && s.Points is { Count: > 0 });

        var vertices = new List<SKPoint>();
        var segments = new List<(SKPoint A, SKPoint B)>();
        foreach (var wall in walls)
        {
            var points = WallGeometry.AbsolutePoints(wall);
            vertices.AddRange(points);
            segments.AddRange(WallGeometry.Segments(wall));
        }

        for (var i = 0; i < ownPoints.Count; i++)
        {
            if (previous is { } prev && ownPoints[i] == prev)
            {
                continue; // the point we start from is not a target
            }

            vertices.Add(ownPoints[i]);
        }

        for (var i = 0; i + 1 < ownPoints.Count; i++)
        {
            segments.Add((ownPoints[i], ownPoints[i + 1]));
        }

        if (!options.Enabled && !forceAxis)
        {
            return Finish(result, previous);
        }

        // ---- 1. points: corners, midpoints, on a wall ----------------------------------------------------
        if (options.Enabled)
        {
            if (options.Endpoints && Nearest(vertices, cursor, tol) is { } corner)
            {
                result.Point = corner;
                result.Snapped = true;
                result.PointKind = SnapKind.Corner;
                return Finish(result, previous);
            }

            if (options.Midpoints)
            {
                var middles = segments.Select(s => new SKPoint((s.A.X + s.B.X) / 2, (s.A.Y + s.B.Y) / 2)).ToList();
                if (Nearest(middles, cursor, tol) is { } middle)
                {
                    result.Point = middle;
                    result.Snapped = true;
                    result.PointKind = SnapKind.Midpoint;
                    return Finish(result, previous);
                }
            }

            if (options.OnWall)
            {
                SKPoint? bestOn = null;
                var bestDistance = tol;
                foreach (var (a, b) in segments)
                {
                    var p = Project(a, b, cursor);
                    var d = Distance(p, cursor);
                    if (d <= bestDistance)
                    {
                        bestDistance = d;
                        bestOn = p;
                    }
                }

                if (bestOn is { } onWall)
                {
                    result.Point = onWall;
                    result.Snapped = true;
                    result.PointKind = SnapKind.OnWall;
                    return Finish(result, previous);
                }
            }
        }

        // points the new wall can line up with: corners of walls, its own points and the corners / centre of other shapes (only built when
        // alignment is on)
        var alignPoints = new List<SKPoint>();
        if (options.Enabled && options.Align)
        {
            alignPoints.AddRange(vertices);
            foreach (var shape in visible)
            {
                if (shape.Points is null && shape.Kind is not (ShapeKind.Dimension or ShapeKind.DesignGrid))
                {
                    var box = ShapeOutline.Box(shape);
                    alignPoints.Add(new SKPoint((float)box.X, (float)box.Y));
                    alignPoints.Add(new SKPoint((float)(box.X + box.W), (float)box.Y));
                    alignPoints.Add(new SKPoint((float)box.X, (float)(box.Y + box.H)));
                    alignPoints.Add(new SKPoint((float)(box.X + box.W), (float)(box.Y + box.H)));
                    alignPoints.Add(new SKPoint((float)(box.X + box.W / 2), (float)(box.Y + box.H / 2)));
                }
            }
        }

        // ---- 2. no previous point: only line up with other points ----------------------------------------
        if (previous is null)
        {
            if (options.Enabled && options.Align)
            {
                AlignFree(result, cursor, alignPoints, tol);
            }

            return Finish(result, null);
        }

        var origin = previous.Value;
        var vector = new SKPoint(cursor.X - origin.X, cursor.Y - origin.Y);
        var length = MathF.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
        if (length < 1e-3f)
        {
            return Finish(result, previous);
        }

        // ---- 3. direction: horizontal / vertical, angle steps, parallel and perpendicular ---------------
        SKPoint? direction = null;
        var directionKind = SnapKind.None;
        var bestPerpendicular = tol;

        void Consider(SKPoint d, SnapKind kind)
        {
            var dot = vector.X * d.X + vector.Y * d.Y;
            if (dot <= 0)
            {
                return;
            }

            var perpendicular = MathF.Abs(vector.X * d.Y - vector.Y * d.X);
            if (perpendicular <= bestPerpendicular)
            {
                bestPerpendicular = perpendicular;
                direction = d;
                directionKind = kind;
            }
        }

        if (forceAxis)
        {
            var d = MathF.Abs(vector.X) >= MathF.Abs(vector.Y)
                ? new SKPoint(vector.X >= 0 ? 1 : -1, 0)
                : new SKPoint(0, vector.Y >= 0 ? 1 : -1);
            direction = d;
            directionKind = SnapKind.Axis;
        }
        else if (options.Enabled)
        {
            if (options.Axis)
            {
                foreach (var axis in Axes)
                {
                    Consider(axis, SnapKind.Axis);
                }
            }

            if (options.AngleSteps && options.AngleStepDegrees >= 1)
            {
                for (var degrees = 0.0; degrees < 360; degrees += options.AngleStepDegrees)
                {
                    var rad = degrees * Math.PI / 180;
                    Consider(new SKPoint((float)Math.Cos(rad), (float)Math.Sin(rad)), SnapKind.Angle);
                }
            }

            if (options.Parallel)
            {
                foreach (var (a, b) in segments)
                {
                    var sx = b.X - a.X;
                    var sy = b.Y - a.Y;
                    var l = MathF.Sqrt(sx * sx + sy * sy);
                    if (l < 1)
                    {
                        continue;
                    }

                    var d = new SKPoint(sx / l, sy / l);
                    var back = new SKPoint(-d.X, -d.Y);
                    var side = new SKPoint(-d.Y, d.X);
                    var otherSide = new SKPoint(d.Y, -d.X);
                    Consider(d, SnapKind.Parallel);
                    Consider(back, SnapKind.Parallel);
                    Consider(side, SnapKind.Parallel);
                    Consider(otherSide, SnapKind.Parallel);
                }
            }
        }

        var point = cursor;
        var finalLength = length;
        var snappedSomething = false;
        var unitDirection = new SKPoint(vector.X / length, vector.Y / length);

        if (direction is { } dir)
        {
            unitDirection = dir;
            finalLength = Math.Max(0, vector.X * dir.X + vector.Y * dir.Y);
            point = new SKPoint(origin.X + dir.X * finalLength, origin.Y + dir.Y * finalLength);
            snappedSomething = true;
            result.PointKind = directionKind;
        }

        if (options.Enabled)
        {
            // ---- 4. line up with other points (along the ray when the direction is fixed) -----------------
            var aligned = false;
            if (options.Align)
            {
                aligned = direction is not null
                    ? AlignAlongRay(result, origin, unitDirection, point, alignPoints, tol, ref point, ref finalLength)
                    : AlignFree(result, point, alignPoints, tol, ref point);
                snappedSomething |= aligned;
            }

            // ---- 5. length: equal to another segment, or rounded -------------------------------------------
            if (!aligned)
            {
                float? target = null;
                var kind = SnapKind.None;
                if (options.EqualLength)
                {
                    var best = tol;
                    foreach (var (a, b) in segments)
                    {
                        var l = Distance(a, b);
                        var delta = MathF.Abs(l - finalLength);
                        if (l > 1 && delta <= best)
                        {
                            best = delta;
                            target = l;
                            kind = SnapKind.EqualLength;
                        }
                    }
                }

                if (target is null && options.RoundLength && options.LengthStepPx > 0.5)
                {
                    var step = (float)options.LengthStepPx;
                    var rounded = MathF.Round(finalLength / step) * step;
                    if (rounded > 1)
                    {
                        target = rounded;
                        kind = SnapKind.RoundLength;
                    }
                }

                if (target is { } wanted)
                {
                    finalLength = wanted;
                    point = new SKPoint(origin.X + unitDirection.X * wanted, origin.Y + unitDirection.Y * wanted);
                    snappedSomething = true;
                    result.Guides.Add(new SnapGuide(kind, origin, point));
                    result.PointKind = kind;
                }
            }
        }

        if (direction is not null)
        {
            // a long dashed line along the locked direction
            var far = Math.Max(finalLength, 40f) + 400;
            result.Guides.Insert(0, new SnapGuide(directionKind,
                new SKPoint(origin.X - unitDirection.X * 400, origin.Y - unitDirection.Y * 400),
                new SKPoint(origin.X + unitDirection.X * far, origin.Y + unitDirection.Y * far)));
        }

        result.Point = point;
        result.Snapped = snappedSomething;
        return Finish(result, origin);
    }

    // ----- helpers ---------------------------------------------------------------------------------------

    private static readonly SKPoint[] Axes = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    private static float Distance(SKPoint a, SKPoint b) => Geo.Distance(a, b);

    private static SKPoint? Nearest(List<SKPoint> points, SKPoint cursor, float tolerance)
    {
        SKPoint? best = null;
        var bestDistance = tolerance;
        foreach (var p in points)
        {
            var d = Distance(p, cursor);
            if (d <= bestDistance)
            {
                bestDistance = d;
                best = p;
            }
        }

        return best;
    }

    private static SKPoint Project(SKPoint a, SKPoint b, SKPoint p) => Geo.ClosestOnSegment(a, b, p);

    /// <summary>Lines the point up with the x and/or y of other points, independently.</summary>
    private static bool AlignFree(WallSnapResult result, SKPoint cursor, List<SKPoint> points, float tolerance)
    {
        var point = cursor;
        return AlignFree(result, cursor, points, tolerance, ref point) && Apply(result, point);
    }

    private static bool Apply(WallSnapResult result, SKPoint point)
    {
        result.Point = point;
        result.Snapped = true;
        result.PointKind = SnapKind.Align;
        return true;
    }

    private static bool AlignFree(WallSnapResult result, SKPoint cursor, List<SKPoint> points, float tolerance, ref SKPoint point)
    {
        SKPoint? columnPoint = null;
        SKPoint? rowPoint = null;
        var bestColumn = tolerance;
        var bestRow = tolerance;
        foreach (var q in points)
        {
            var dx = MathF.Abs(cursor.X - q.X);
            if (dx <= bestColumn)
            {
                bestColumn = dx;
                columnPoint = q;
            }

            var dy = MathF.Abs(cursor.Y - q.Y);
            if (dy <= bestRow)
            {
                bestRow = dy;
                rowPoint = q;
            }
        }

        var x = columnPoint?.X ?? cursor.X;
        var y = rowPoint?.Y ?? cursor.Y;
        point = new SKPoint(x, y);

        if (columnPoint is { } c)
        {
            result.Guides.Add(new SnapGuide(SnapKind.Align, c, point));
        }

        if (rowPoint is { } r)
        {
            result.Guides.Add(new SnapGuide(SnapKind.Align, r, point));
        }

        if (columnPoint is not null || rowPoint is not null)
        {
            result.PointKind = SnapKind.Align;
            return true;
        }

        return false;
    }

    /// <summary>With the direction fixed: the point where the ray meets the vertical or horizontal line through another point.</summary>
    private static bool AlignAlongRay(
        WallSnapResult result, SKPoint origin, SKPoint direction, SKPoint current, List<SKPoint> points, float tolerance,
        ref SKPoint point, ref float length)
    {
        SKPoint? bestPoint = null;
        SKPoint? bestSource = null;
        var bestDistance = tolerance;

        foreach (var q in points)
        {
            if (MathF.Abs(direction.X) > 1e-4f)
            {
                var t = (q.X - origin.X) / direction.X;
                if (t > 1)
                {
                    var p = new SKPoint(origin.X + direction.X * t, origin.Y + direction.Y * t);
                    var d = Distance(p, current);
                    if (d <= bestDistance)
                    {
                        bestDistance = d;
                        bestPoint = p;
                        bestSource = q;
                    }
                }
            }

            if (MathF.Abs(direction.Y) > 1e-4f)
            {
                var t = (q.Y - origin.Y) / direction.Y;
                if (t > 1)
                {
                    var p = new SKPoint(origin.X + direction.X * t, origin.Y + direction.Y * t);
                    var d = Distance(p, current);
                    if (d <= bestDistance)
                    {
                        bestDistance = d;
                        bestPoint = p;
                        bestSource = q;
                    }
                }
            }
        }

        if (bestPoint is { } found && bestSource is { } source)
        {
            point = found;
            length = Distance(origin, found);
            result.Guides.Add(new SnapGuide(SnapKind.Align, source, found));
            result.PointKind = SnapKind.Align;
            return true;
        }

        return false;
    }

    /// <summary>What a snap is called in the readout.</summary>
    public static string Describe(SnapKind kind) => kind switch
    {
        SnapKind.Corner => "corner",
        SnapKind.Midpoint => "midpoint",
        SnapKind.OnWall => "on wall",
        SnapKind.Axis => "horizontal / vertical",
        SnapKind.Angle => "angle step",
        SnapKind.Parallel => "parallel / perpendicular",
        SnapKind.Align => "aligned",
        SnapKind.EqualLength => "equal length",
        SnapKind.RoundLength => "rounded",
        _ => string.Empty,
    };

    private static WallSnapResult Finish(WallSnapResult result, SKPoint? origin)
    {
        if (origin is { } from)
        {
            var length = Geo.Distance(from, result.Point);
            var angle = Geo.AngleDegrees(from, result.Point);
            var what = Describe(result.PointKind);
            result.Label = WallGeometry.FormatLength(length, LengthUnit.Centimeter) + "  " + angle.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "°"
                + (what.Length > 0 ? "  · " + what : string.Empty);
        }
        else
        {
            // with no start point only point snaps are named
            result.Label = result.PointKind is SnapKind.Corner or SnapKind.Midpoint or SnapKind.OnWall or SnapKind.Align
                ? Describe(result.PointKind)
                : string.Empty;
        }

        return result;
    }
}
