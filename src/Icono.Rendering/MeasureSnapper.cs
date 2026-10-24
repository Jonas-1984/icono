using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

public enum MeasureSnapKind
{
    None,

    /// <summary>A corner of an outline (for a wall: of either face) or a vertex of a line.</summary>
    Corner,

    /// <summary>The middle of a straight edge.</summary>
    Midpoint,

    /// <summary>The centre of a shape.</summary>
    Center,

    /// <summary>One of a shape's 16 connection points.</summary>
    Connection,

    /// <summary>Anywhere on an edge: the nearest place on the outline.</summary>
    Edge,
}

/// <summary>A place the measuring tools snapped to. <see cref="Anchor"/> is set when the place can be pinned to its shape (corners,
/// middles, centres, connection points); an edge point is free.</summary>
public sealed record MeasureSnap(SKPoint Point, MeasureSnapKind Kind, Shape Shape, ShapeAnchor? Anchor);

/// <summary>Finds the corner, middle, centre or edge of any shape or wall under the pointer, so a dimension or the ruler can start and end
/// exactly anywhere on the drawing.</summary>
public static class MeasureSnapper
{
    /// <param name="tolerance">How near (document px) the pointer has to be.</param>
    /// <param name="includeEdges">Also snap to the nearest place on an edge when no corner, middle or centre is near.</param>
    public static MeasureSnap? Find(Document document, SKPoint cursor, double tolerance, bool includeEdges = true, Shape? exclude = null)
    {
        var tol = (float)tolerance;
        MeasureSnap? best = null;
        var bestRank = int.MaxValue;
        var bestDistance = float.MaxValue;

        void Consider(SKPoint point, MeasureSnapKind kind, int rank, Shape shape, ShapeAnchor? anchor)
        {
            var distance = Distance(point, cursor);
            if (distance > tol)
            {
                return;
            }

            if (rank < bestRank || (rank == bestRank && distance < bestDistance))
            {
                bestRank = rank;
                bestDistance = distance;
                best = new MeasureSnap(point, kind, shape, anchor);
            }
        }

        // only shapes the pointer is near (cheap frame test) take part, in both passes
        var shapes = new List<Shape>();
        foreach (var shape in document.VisibleShapes())
        {
            if (!ReferenceEquals(shape, exclude) && OutlineGeometry.IsMeasurable(shape) && IsNear(shape, cursor, tol))
            {
                shapes.Add(shape);
            }
        }

        foreach (var shape in shapes)
        {

            // 1. corners: the outline's own (walls: both faces), and a line's vertices
            var corners = OutlineGeometry.Corners(shape);
            for (var i = 0; i < corners.Count; i++)
            {
                Consider(corners[i], MeasureSnapKind.Corner, 0, shape, new ShapeAnchor(shape.Id, OutlineGeometry.CornerBase + i));
            }

            if (shape.Points is { Count: > 0 } points)
            {
                for (var i = 0; i < points.Count; i++)
                {
                    Consider(new SKPoint((float)(shape.X + points[i].X), (float)(shape.Y + points[i].Y)), MeasureSnapKind.Corner, 0, shape,
                        new ShapeAnchor(shape.Id, i));
                }
            }

            // 2. middle of a straight edge
            var middles = OutlineGeometry.EdgeMiddles(shape);
            for (var i = 0; i < middles.Count; i++)
            {
                Consider(middles[i], MeasureSnapKind.Midpoint, 1, shape, new ShapeAnchor(shape.Id, OutlineGeometry.MidBase + i));
            }

            // 3. centre and connection points of a plain shape
            if (shape.Points is null)
            {
                var centre = DimensionRouter.AnchorPosition(shape, DimensionRouter.CenterIndex);
                Consider(new SKPoint((float)centre.X, (float)centre.Y), MeasureSnapKind.Center, 2, shape, new ShapeAnchor(shape.Id, DimensionRouter.CenterIndex));
                for (var i = 0; i < ConnectionRouter.PointCount; i++)
                {
                    var p = ConnectionRouter.GetPoint(shape, i);
                    Consider(new SKPoint((float)p.X, (float)p.Y), MeasureSnapKind.Connection, 3, shape, new ShapeAnchor(shape.Id, i));
                }
            }
        }

        if (best is not null || !includeEdges)
        {
            return best;
        }

        // 4. anywhere on an edge
        foreach (var shape in shapes)
        {
            if (OutlineGeometry.NearestOnOutline(shape, cursor) is { } near)
            {
                Consider(near.Point, MeasureSnapKind.Edge, 4, shape, null);
            }
        }

        return best;
    }

    private static float Distance(SKPoint a, SKPoint b) => Geo.Distance(a, b);

    /// <summary>The pointer is within reach of the shape's frame (a wall's frame is its centre line, so add its thickness).</summary>
    private static bool IsNear(Shape shape, SKPoint cursor, float tolerance)
    {
        var frame = ShapeOutline.Box(shape);
        var reach = tolerance + (shape.Kind == ShapeKind.Wall ? shape.WallThickness : 0) + 2;
        return cursor.X >= frame.X - reach && cursor.X <= frame.X + frame.W + reach &&
               cursor.Y >= frame.Y - reach && cursor.Y <= frame.Y + frame.H + reach;
    }
}
