using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Which dimensions "Add dimensions" creates for a shape.</summary>
public enum DimensionSet
{
    /// <summary>What suits the shape: width/height, diameter/radius, segments and angles, or a curve's length.</summary>
    Auto,

    /// <summary>Width, height (and depth of a 3D box).</summary>
    Size,

    /// <summary>Diameter and radius of a circle.</summary>
    Round,

    /// <summary>The angle at every corner.</summary>
    Corners,

    /// <summary>The length of every segment (or of a curve).</summary>
    Segments,
}

/// <summary>Works out the dimensions of a shape: where their points lie and what they measure. The same maths drives the
/// creation of the dimensions and, later, keeps them attached when the shape changes (<see cref="DimensionRouter"/>).</summary>
public static class DimensionBuilder
{
    public sealed record Result(DimensionKind Kind, List<(double X, double Y)> Points, double Value);

    /// <summary>Spacing between the shape and its first dimension line.</summary>
    public const double DefaultOffset = 28;

    /// <summary>The roles a set asks for, as (role, index) pairs.</summary>
    public static List<(DimensionRole Role, int Index)> Plan(Shape target, DimensionSet set)
    {
        var plan = new List<(DimensionRole, int)>();
        if (target.Kind is ShapeKind.Dimension or ShapeKind.Wall or ShapeKind.Text or ShapeKind.DesignGrid)
        {
            return plan;
        }

        if (target.Points is { Count: >= 2 } points)
        {
            var closed = IsClosed(target);
            var segments = closed ? points.Count : points.Count - 1;

            void AddSegments()
            {
                for (var i = 0; i < segments; i++)
                {
                    plan.Add((DimensionRole.Segment, i));
                }
            }

            void AddCorners()
            {
                if (points.Count == 2)
                {
                    plan.Add((DimensionRole.Corner, -1)); // angle of a straight line against the horizontal
                    return;
                }

                for (var i = closed ? 0 : 1; i < (closed ? points.Count : points.Count - 1); i++)
                {
                    plan.Add((DimensionRole.Corner, i));
                }
            }

            switch (set)
            {
                case DimensionSet.Size:
                    plan.Add((DimensionRole.Width, 0));
                    plan.Add((DimensionRole.Height, 0));
                    break;
                case DimensionSet.Corners:
                    AddCorners();
                    break;
                case DimensionSet.Segments:
                    if (target.Kind == ShapeKind.Curve || target.PathRoutingStyle == PathRoutingStyle.Curved)
                    {
                        plan.Add((DimensionRole.Length, 0));
                    }
                    else
                    {
                        AddSegments();
                    }

                    break;
                case DimensionSet.Auto:
                    if (target.Kind == ShapeKind.Curve || target.PathRoutingStyle == PathRoutingStyle.Curved)
                    {
                        plan.Add((DimensionRole.Length, 0));
                    }
                    else
                    {
                        AddSegments();
                        AddCorners();
                    }

                    break;
            }

            return plan;
        }

        var outline = ShapeOutline.Box(target);
        var circular = target.Kind == ShapeKind.Ellipse && Math.Abs(outline.W - outline.H) <= Math.Max(outline.W, outline.H) * 0.005;
        var round = target.Kind == ShapeKind.Ellipse;

        switch (set)
        {
            case DimensionSet.Size:
                AddSize();
                break;
            case DimensionSet.Round:
                if (round)
                {
                    plan.Add((DimensionRole.Diameter, 0));
                    plan.Add((DimensionRole.Radius, 0));
                }

                break;
            case DimensionSet.Auto:
                if (circular)
                {
                    plan.Add((DimensionRole.Diameter, 0));
                    plan.Add((DimensionRole.Radius, 0));
                }
                else
                {
                    AddSize();
                }

                break;
        }

        return plan;

        void AddSize()
        {
            plan.Add((DimensionRole.Width, 0));
            plan.Add((DimensionRole.Height, 0));
            if (target.Kind == ShapeKind.Box3D)
            {
                plan.Add((DimensionRole.Depth, 0));
            }
        }
    }

    /// <summary>A closed outline (a converted polygon) as opposed to an open line.</summary>
    public static bool IsClosed(Shape shape) =>
        shape.Points is { Count: >= 3 } && !ShapeGeometryFactory.IsStrokeOnly(shape.Kind) && shape.Kind != ShapeKind.Wall;

    /// <summary>The points and value of one dimension of <paramref name="target"/>, or null when the shape has no such part.</summary>
    public static Result? Compute(Shape target, DimensionRole role, int index, double offset, double angleDegrees)
    {
        return role switch
        {
            DimensionRole.Width or DimensionRole.Height or DimensionRole.Depth => ComputeSize(target, role, offset),
            DimensionRole.Diameter or DimensionRole.Radius => ComputeRound(target, role, angleDegrees),
            DimensionRole.Segment => ComputeSegment(target, index, offset),
            DimensionRole.Corner => ComputeCorner(target, index, offset),
            DimensionRole.Length => ComputeLength(target, offset),
            _ => null,
        };
    }

    private static (double X, double Y) Rotate((double X, double Y) p, Shape shape)
    {
        if (shape.Rotation == 0)
        {
            return p;
        }

        var cx = shape.X + shape.Width / 2;
        var cy = shape.Y + shape.Height / 2;
        var rad = shape.Rotation * Math.PI / 180.0;
        var dx = p.X - cx;
        var dy = p.Y - cy;
        return (cx + dx * Math.Cos(rad) - dy * Math.Sin(rad), cy + dx * Math.Sin(rad) + dy * Math.Cos(rad));
    }

    private static Result? ComputeSize(Shape target, DimensionRole role, double offset)
    {
        if (target.Kind == ShapeKind.Box3D)
        {
            var d = Math.Min(target.Depth3D, Math.Min(target.Width, target.Height) * 0.6);
            var x = target.X;
            var y = target.Y;
            var w = target.Width;
            var h = target.Height;
            var k = 0.70710678;
            return role switch
            {
                DimensionRole.Width => Linear(target, (x, y + h), (x + w - d, y + h), 0, 1, offset, w - d),
                DimensionRole.Height => Linear(target, (x, y + h), (x, y + d), -1, 0, offset, h - d),
                _ => Linear(target, (x + w - d, y + h), (x + w, y + h - d), k, k, offset, d),
            };
        }

        // Corners of the drawn outline: top-left, top-right, bottom-right, bottom-left.
        (double X, double Y) tl;
        (double X, double Y) tr;
        (double X, double Y) br;
        (double X, double Y) bl;
        if (target.Points is null)
        {
            tl = ConnectionRouter.GetPoint(target, 0);
            tr = ConnectionRouter.GetPoint(target, 4);
            br = ConnectionRouter.GetPoint(target, 8);
            bl = ConnectionRouter.GetPoint(target, 12);
        }
        else
        {
            var box = ShapeOutline.Box(target);
            tl = (box.X, box.Y);
            tr = (box.X + box.W, box.Y);
            br = (box.X + box.W, box.Y + box.H);
            bl = (box.X, box.Y + box.H);
        }

        if (role == DimensionRole.Width)
        {
            var u = Unit(bl, br);
            return Linear(null, bl, br, -u.Y, u.X, offset, null);
        }

        if (role == DimensionRole.Height)
        {
            var u = Unit(br, tr);
            return Linear(null, br, tr, -u.Y, u.X, offset, null);
        }

        return null;
    }

    private static (double X, double Y) Unit((double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var l = Math.Sqrt(dx * dx + dy * dy);
        return l < 1e-9 ? (1, 0) : (dx / l, dy / l);
    }

    /// <summary>An aligned dimension from a to b whose line sits <paramref name="offset"/> away in direction (nx, ny).
    /// <paramref name="rotateWith"/> turns the points with that shape's rotation (box faces are built unrotated).</summary>
    private static Result Linear(Shape? rotateWith, (double X, double Y) a, (double X, double Y) b, double nx, double ny, double offset, double? value)
    {
        var length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        var third = (a.X + nx * offset, a.Y + ny * offset);

        if (rotateWith is not null)
        {
            a = Rotate(a, rotateWith);
            b = Rotate(b, rotateWith);
            third = Rotate(third, rotateWith);
        }

        return new Result(DimensionKind.Aligned, [a, b, third], value ?? length);
    }

    private static Result? ComputeRound(Shape target, DimensionRole role, double angleDegrees)
    {
        if (target.Kind != ShapeKind.Ellipse)
        {
            return null;
        }

        var box = ShapeOutline.Box(target);
        var center = Rotate((box.X + box.W / 2, box.Y + box.H / 2), target);
        var radius = Math.Max(box.W, box.H) / 2;
        var rad = (angleDegrees + target.Rotation) * Math.PI / 180.0;
        var edge = (center.Item1 + Math.Cos(rad) * radius, center.Item2 + Math.Sin(rad) * radius);

        return role == DimensionRole.Diameter
            ? new Result(DimensionKind.Diameter, [center, edge], radius * 2)
            : new Result(DimensionKind.Radius, [center, edge], radius);
    }

    private static List<(double X, double Y)>? Absolute(Shape target)
    {
        if (target.Points is null)
        {
            return null;
        }

        return target.Points.Select(p => (target.X + p.X, target.Y + p.Y)).ToList();
    }

    private static Result? ComputeSegment(Shape target, int index, double offset)
    {
        var pts = Absolute(target);
        if (pts is null || pts.Count < 2)
        {
            return null;
        }

        var closed = IsClosed(target);
        var count = closed ? pts.Count : pts.Count - 1;
        if (index < 0 || index >= count)
        {
            return null;
        }

        var a = pts[index];
        var b = pts[(index + 1) % pts.Count];
        var u = Unit(a, b);
        var side = 1.0;
        if (closed)
        {
            // keep the numbers outside the outline
            double area = 0;
            for (var i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                var q = pts[(i + 1) % pts.Count];
                area += p.X * q.Y - q.X * p.Y;
            }

            side = area > 0 ? 1.0 : -1.0;
        }

        // The normal (-uy, ux) lies to the right of the way on screen; for a clockwise outline that is the outside.
        var nx = -u.Y * side;
        var ny = u.X * side;
        var third = (a.X + nx * offset, a.Y + ny * offset);
        var length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        return new Result(DimensionKind.Aligned, [a, b, third], length);
    }

    private static Result? ComputeCorner(Shape target, int index, double offset)
    {
        var pts = Absolute(target);
        if (pts is null || pts.Count < 2)
        {
            return null;
        }

        var radius = Math.Max(14, offset);
        if (index == -1)
        {
            // angle of a straight line against the horizontal
            var v = pts[0];
            var end = pts[^1];
            var length = Math.Sqrt((end.X - v.X) * (end.X - v.X) + (end.Y - v.Y) * (end.Y - v.Y));
            if (length < 1)
            {
                return null;
            }

            var a = (v.X + Math.Max(length, radius + 10) * (end.X >= v.X ? 1 : -1), v.Y);
            var mid = Math.Atan2(end.Y - v.Y, end.X - v.X) / 2 + (end.X >= v.X ? 0 : Math.PI / 2);
            var c = (v.X + Math.Cos(mid) * radius, v.Y + Math.Sin(mid) * radius);
            return new Result(DimensionKind.Angular, [v, a, end, c], 0);
        }

        var closed = IsClosed(target);
        if (index < 0 || index >= pts.Count || (!closed && (index == 0 || index == pts.Count - 1)))
        {
            return null;
        }

        var vertex = pts[index];
        var prev = pts[(index - 1 + pts.Count) % pts.Count];
        var next = pts[(index + 1) % pts.Count];
        var ua = Unit(vertex, prev);
        var ub = Unit(vertex, next);
        var bx = ua.X + ub.X;
        var by = ua.Y + ub.Y;
        var bl = Math.Sqrt(bx * bx + by * by);
        (double X, double Y) bis = bl < 1e-6 ? (-ua.Y, ua.X) : (bx / bl, by / bl);
        var arc = (vertex.X + bis.X * radius, vertex.Y + bis.Y * radius);
        return new Result(DimensionKind.Angular, [vertex, prev, next, arc], 0);
    }

    private static Result? ComputeLength(Shape target, double offset)
    {
        using var path = ShapeGeometryFactory.Build(target);
        using var measure = new SKPathMeasure(path, false);
        var length = measure.Length;
        if (length < 1)
        {
            return null;
        }

        if (!measure.GetPositionAndTangent(length / 2, out var position, out var tangent))
        {
            return null;
        }

        // The text goes beside the middle of the curve, on its left.
        var nx = -tangent.Y;
        var ny = tangent.X;
        var anchor = (position.X, position.Y);
        var text = (position.X + nx * offset, position.Y + ny * offset);
        return new Result(DimensionKind.Length, [anchor, text], length);
    }

    /// <summary>Creates the dimension shapes for <paramref name="target"/> (not yet in any layer).</summary>
    public static List<Shape> Create(Shape target, DimensionSet set, LengthUnit unit = LengthUnit.Centimeter)
    {
        var result = new List<Shape>();
        var step = 0;
        foreach (var (role, index) in Plan(target, set))
        {
            var offset = DefaultOffset + (role is DimensionRole.Width or DimensionRole.Height ? 0 : 0);
            var angle = role == DimensionRole.Diameter ? -45.0 : role == DimensionRole.Radius ? -135.0 : 0.0;
            var computed = Compute(target, role, index, offset, angle);
            if (computed is null)
            {
                continue;
            }

            var dim = new Shape { Kind = ShapeKind.Dimension };
            ApplyStyle(dim);
            dim.DimKind = computed.Kind;
            dim.DimRole = role;
            dim.DimIndex = index;
            dim.DimTarget = target.Id;
            dim.DimOffset = offset;
            dim.DimAngle = angle;
            dim.DimUnit = unit;
            Place(dim, computed);
            result.Add(dim);
            step++;
        }

        _ = step;
        return result;
    }

    /// <summary>The default look of a dimension: thin red lines, small text, no fill.</summary>
    public static void ApplyStyle(Shape dim)
    {
        dim.FillEnabled = false;
        dim.BorderColor = ArgbColor.FromRgb(0xC0, 0x39, 0x2B);
        dim.BorderThickness = 1;
        dim.TextFontSize = 12;
    }

    /// <summary>Gives a dimension shape the points and value of a computed result.</summary>
    public static void Place(Shape dim, Result computed)
    {
        dim.DimKind = computed.Kind;
        dim.DimValue = computed.Value;
        dim.X = computed.Points[0].X;
        dim.Y = computed.Points[0].Y;
        dim.Points = computed.Points.Select(p => new CorePoint(p.X - dim.X, p.Y - dim.Y)).ToList();
        PathBoundsCalculator.Recompute(dim);
    }
}
