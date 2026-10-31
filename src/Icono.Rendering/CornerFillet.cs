using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>The Curve tool works like the fillet of a CAD program: pick one side of a corner, pick the neighbouring side, give the size - and just that
/// corner is curved. These helpers find the sides and the corner of a line or shape and set the radius of a single corner
/// (<see cref="Shape.PointRadii"/>). A shape with straight sides but no points (a rectangle) becomes a shape of points the first time.</summary>
public static class CornerFillet
{
    /// <summary>The corners of the outline in page px (as drawn, with the rotation of the shape), or null when the shape has none the tool can curve.</summary>
    public static List<SKPoint>? Vertices(Shape shape)
    {
        if (!ShapeGeometryFactory.CanRoundCorners(shape))
        {
            return null;
        }

        IReadOnlyList<CorePoint>? local = shape.Points;
        if (local is null)
        {
            if (!ShapeGeometryFactory.SupportsPointEditing(shape.Kind))
            {
                return null;
            }

            local = ShapeGeometryFactory.ExtractEditablePoints(shape);
        }

        var result = local.Select(p => new SKPoint((float)(shape.X + p.X), (float)(shape.Y + p.Y))).ToList();
        if (shape.Rotation != 0)
        {
            var turn = SKMatrix.CreateRotationDegrees((float)shape.Rotation, (float)(shape.X + shape.Width / 2), (float)(shape.Y + shape.Height / 2));
            for (var i = 0; i < result.Count; i++)
            {
                result[i] = turn.MapPoint(result[i]);
            }
        }

        return result;
    }

    /// <summary>A shape of straight sides is closed (its last corner joins its first); a line is open.</summary>
    public static bool IsClosed(Shape shape) => !ShapeGeometryFactory.IsStrokeOnly(shape.Kind);

    /// <summary>The number of sides: one between each two neighbouring corners, and for a closed outline one back to the start.</summary>
    public static int SideCount(int vertexCount, bool closed) => closed ? vertexCount : vertexCount - 1;

    /// <summary>The side nearest to the point (its index: side <c>s</c> joins corner <c>s</c> and the next one), or null when none is within
    /// <paramref name="tolerance"/>.</summary>
    public static int? NearestSide(Shape shape, SKPoint point, float tolerance)
    {
        if (Vertices(shape) is not { Count: >= 3 } vertices)
        {
            return null;
        }

        var closed = IsClosed(shape);
        int? best = null;
        var bestDistance = tolerance;
        for (var side = 0; side < SideCount(vertices.Count, closed); side++)
        {
            var (a, b) = Ends(vertices, side);
            var distance = Geo.Distance(Geo.ClosestOnSegment(a, b, point), point);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = side;
            }
        }

        return best;
    }

    /// <summary>The two ends of a side.</summary>
    public static (SKPoint A, SKPoint B) Ends(IReadOnlyList<SKPoint> vertices, int side) => (vertices[side], vertices[(side + 1) % vertices.Count]);

    /// <summary>The corner where two sides meet (its index), or null when they do not touch, are the same side, or meet in the end of an open line,
    /// which has no corner.</summary>
    public static int? SharedCorner(int vertexCount, bool closed, int sideA, int sideB)
    {
        if (sideA == sideB)
        {
            return null;
        }

        int[] a = [sideA, (sideA + 1) % vertexCount];
        int[] b = [sideB, (sideB + 1) % vertexCount];
        foreach (var corner in a.Intersect(b))
        {
            if (closed || (corner > 0 && corner < vertexCount - 1))
            {
                return corner;
            }
        }

        return null;
    }

    /// <summary>Gives one corner the radius (page px; 0 = sharp). Returns false when the shape cannot be curved or the corner does not exist. A shape
    /// without points gets them first (the same points "Edit Points" would give it).</summary>
    public static bool Apply(Shape shape, int corner, double radius)
    {
        if (!ShapeGeometryFactory.CanRoundCorners(shape))
        {
            return false;
        }

        if (shape.Points is null)
        {
            if (!ShapeGeometryFactory.SupportsPointEditing(shape.Kind))
            {
                return false;
            }

            shape.Points = ShapeGeometryFactory.ExtractEditablePoints(shape);
            PathBoundsCalculator.Recompute(shape);
        }

        var count = shape.Points!.Count;
        if (corner < 0 || corner >= count)
        {
            return false;
        }

        var radii = shape.PointRadii is { } existing && existing.Count == count ? existing.ToList() : Enumerable.Repeat(0.0, count).ToList();
        radii[corner] = Math.Max(0, radius);
        shape.PointRadii = radii.Any(r => r > 0) ? radii : null;
        return true;
    }

    /// <summary>The radii after a point was inserted after <paramref name="index"/> (the new corner is sharp).</summary>
    public static IReadOnlyList<double>? AfterInsert(IReadOnlyList<double>? radii, int pointCountBefore, int index)
    {
        if (radii is null || radii.Count != pointCountBefore)
        {
            return null;
        }

        var list = radii.ToList();
        list.Insert(Math.Min(index + 1, list.Count), 0);
        return list;
    }

    /// <summary>The radii after the point at <paramref name="index"/> was removed.</summary>
    public static IReadOnlyList<double>? AfterRemove(IReadOnlyList<double>? radii, int pointCountBefore, int index)
    {
        if (radii is null || radii.Count != pointCountBefore || index < 0 || index >= radii.Count)
        {
            return null;
        }

        var list = radii.ToList();
        list.RemoveAt(index);
        return list.Any(r => r > 0) ? list : null;
    }
}
