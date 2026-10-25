using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

public class PointSnapTests
{
    private static Document DocumentWith(params Shape[] shapes)
    {
        var document = new Document { Width = 800, Height = 600 };
        var layer = new Layer { Name = "L" };
        foreach (var shape in shapes)
        {
            layer.Shapes.Add(shape);
        }

        document.Layers.Add(layer);
        return document;
    }

    private static Shape Line(double x1, double y1, double x2, double y2)
    {
        var line = new Shape { Kind = ShapeKind.Line, X = x1, Y = y1 };
        line.Points = [new CorePoint(0, 0), new CorePoint(x2 - x1, y2 - y1)];
        PathBoundsCalculator.Recompute(line);
        return line;
    }

    private static PointSnapOptions Only(params PointSnapKind[] kinds)
    {
        var options = new PointSnapOptions();
        foreach (var kind in Enum.GetValues<PointSnapKind>())
        {
            options.Set(kind, kinds.Contains(kind));
        }

        return options;
    }

    private static PointSnapResult? Snap(Document document, float x, float y, PointSnapOptions options, SKPoint? reference = null, params Shape[] exclude) =>
        PointSnapper.Snap(document, new SKPoint(x, y), 8, options, exclude, reference);

    [Fact]
    public void Endpoint_SnapsToTheEndOfALine()
    {
        var result = Snap(DocumentWith(Line(100, 100, 200, 100)), 203, 102, Only(PointSnapKind.Endpoint));

        Assert.NotNull(result);
        Assert.Equal(PointSnapKind.Endpoint, result!.Kind);
        Assert.Equal(200, result.Point.X, 3);
        Assert.Equal(100, result.Point.Y, 3);
    }

    [Fact]
    public void Endpoint_SnapsToTheCornerOfAShape()
    {
        var rectangle = new Shape { Kind = ShapeKind.Rectangle, X = 100, Y = 100, Width = 200, Height = 120 };
        using var path = ShapeGeometryFactory.Build(rectangle);
        var corner = new SKPoint(path.Bounds.Right, path.Bounds.Bottom);

        var result = Snap(DocumentWith(rectangle), corner.X + 3, corner.Y - 2, Only(PointSnapKind.Endpoint));

        Assert.Equal(PointSnapKind.Endpoint, result!.Kind);
        Assert.Equal(corner.X, result.Point.X, 2);
        Assert.Equal(corner.Y, result.Point.Y, 2);
    }

    [Fact]
    public void Endpoint_SnapsToTheOuterCornerOfAWall()
    {
        var wall = new Shape { Kind = ShapeKind.Wall, X = 100, Y = 100, WallThickness = 16 };
        wall.Points = [new CorePoint(0, 0), new CorePoint(200, 0), new CorePoint(200, 200)];
        PathBoundsCalculator.Recompute(wall);

        // the outer corner of the band is 8 px beyond the corner of the centre line
        var result = Snap(DocumentWith(wall), 311, 91, Only(PointSnapKind.Endpoint));

        Assert.Equal(PointSnapKind.Endpoint, result!.Kind);
        Assert.Equal(308, result.Point.X, 2);
        Assert.Equal(92, result.Point.Y, 2);
    }

    [Fact]
    public void Midpoint_SnapsToTheMiddleOfALine()
    {
        var result = Snap(DocumentWith(Line(100, 100, 200, 100)), 152, 103, Only(PointSnapKind.Midpoint));

        Assert.Equal(PointSnapKind.Midpoint, result!.Kind);
        Assert.Equal(150, result.Point.X, 3);
        Assert.Equal(100, result.Point.Y, 3);
    }

    [Fact]
    public void CenterAndQuadrant_SnapOnACircle()
    {
        var circle = new Shape { Kind = ShapeKind.Ellipse, X = 100, Y = 100, Width = 100, Height = 100 };
        using var outline = ShapeGeometryFactory.Build(circle);
        var box = (X: (double)outline.Bounds.Left, Y: (double)outline.Bounds.Top, W: (double)outline.Bounds.Width, H: (double)outline.Bounds.Height);
        var centre = new SKPoint(outline.Bounds.MidX, outline.Bounds.MidY);
        var document = DocumentWith(circle);

        var middle = Snap(document, centre.X + 2, centre.Y + 3, Only(PointSnapKind.Center));
        var right = Snap(document, (float)(box.X + box.W) + 2, centre.Y + 2, Only(PointSnapKind.Quadrant));

        Assert.Equal(PointSnapKind.Center, middle!.Kind);
        Assert.Equal(centre.X, middle.Point.X, 2);
        Assert.Equal(PointSnapKind.Quadrant, right!.Kind);
        Assert.Equal(box.X + box.W, right.Point.X, 2);
        Assert.Equal(centre.Y, right.Point.Y, 2);
    }

    [Fact]
    public void GeometricCenter_SnapsToTheCentreOfAClosedShape()
    {
        var rectangle = new Shape { Kind = ShapeKind.Rectangle, X = 100, Y = 100, Width = 200, Height = 120 };
        using var path = ShapeGeometryFactory.Build(rectangle);

        var result = Snap(DocumentWith(rectangle), path.Bounds.MidX + 3, path.Bounds.MidY - 3, Only(PointSnapKind.GeometricCenter));

        Assert.Equal(PointSnapKind.GeometricCenter, result!.Kind);
        Assert.Equal(path.Bounds.MidX, result.Point.X, 2);
        Assert.Equal(path.Bounds.MidY, result.Point.Y, 2);
    }

    [Fact]
    public void Intersection_SnapsWhereTwoLinesCross()
    {
        var document = DocumentWith(Line(100, 100, 300, 300), Line(100, 300, 300, 100));

        var result = Snap(document, 203, 198, Only(PointSnapKind.Intersection));

        Assert.Equal(PointSnapKind.Intersection, result!.Kind);
        Assert.Equal(200, result.Point.X, 2);
        Assert.Equal(200, result.Point.Y, 2);
    }

    [Fact]
    public void Extension_SnapsOnTheContinuationOfALine()
    {
        var result = Snap(DocumentWith(Line(100, 100, 200, 100)), 300, 103, Only(PointSnapKind.Extension));

        Assert.Equal(PointSnapKind.Extension, result!.Kind);
        Assert.Equal(300, result.Point.X, 2);
        Assert.Equal(100, result.Point.Y, 2);
        Assert.Single(result.Guides);
        Assert.Equal(200, result.Guides[0].From.X, 2);
    }

    [Fact]
    public void Extension_DoesNotSnapOnTheLineItself()
    {
        var document = DocumentWith(Line(100, 100, 200, 100));

        Assert.Null(Snap(document, 150, 103, Only(PointSnapKind.Extension)));
    }

    [Fact]
    public void Perpendicular_SnapsToTheFootOfTheLineFromTheLastPoint()
    {
        var document = DocumentWith(Line(100, 300, 400, 300));

        var result = Snap(document, 252, 297, Only(PointSnapKind.Perpendicular), new SKPoint(250, 100));

        Assert.Equal(PointSnapKind.Perpendicular, result!.Kind);
        Assert.Equal(250, result.Point.X, 2);
        Assert.Equal(300, result.Point.Y, 2);
    }

    [Fact]
    public void Perpendicular_NeedsALastPoint()
    {
        Assert.Null(Snap(DocumentWith(Line(100, 300, 400, 300)), 252, 297, Only(PointSnapKind.Perpendicular)));
    }

    [Fact]
    public void Tangent_SnapsToTheTouchingPointOfACircle()
    {
        var circle = new Shape { Kind = ShapeKind.Ellipse, X = 250, Y = 250, Width = 100, Height = 100 };
        using var outline = ShapeGeometryFactory.Build(circle);
        var centre = new SKPoint(outline.Bounds.MidX, outline.Bounds.MidY);
        float rx = outline.Bounds.Width / 2, ry = outline.Bounds.Height / 2;
        var from = new SKPoint(centre.X + 2 * rx, centre.Y);

        // the touching point below the axis, found by searching the angle where the line from the point meets the ellipse at a right angle to its normal
        SKPoint expected = default;
        var bestMiss = double.MaxValue;
        for (var degrees = 0.0; degrees < 180; degrees += 0.01)
        {
            var t = degrees * Math.PI / 180;
            var p = new SKPoint(centre.X + rx * (float)Math.Cos(t), centre.Y + ry * (float)Math.Sin(t));
            var normal = ((p.X - centre.X) / (rx * rx), (p.Y - centre.Y) / (ry * ry));
            var miss = Math.Abs((p.X - from.X) * normal.Item1 + (p.Y - from.Y) * normal.Item2);
            if (miss < bestMiss)
            {
                bestMiss = miss;
                expected = p;
            }
        }

        var result = Snap(DocumentWith(circle), expected.X + 1, expected.Y - 1, Only(PointSnapKind.Tangent), from);

        Assert.Equal(PointSnapKind.Tangent, result!.Kind);
        Assert.InRange(Geo.Distance(result.Point, expected), 0, 3.5);
    }

    [Fact]
    public void Nearest_SnapsToTheClosestPointOnTheObject()
    {
        var result = Snap(DocumentWith(Line(100, 100, 300, 100)), 170, 105, Only(PointSnapKind.Nearest));

        Assert.Equal(PointSnapKind.Nearest, result!.Kind);
        Assert.Equal(170, result.Point.X, 2);
        Assert.Equal(100, result.Point.Y, 2);
    }

    [Fact]
    public void ApparentIntersection_SnapsWhereTheContinuationsMeet()
    {
        var document = DocumentWith(Line(100, 100, 200, 100), Line(300, 150, 300, 300));

        var result = Snap(document, 302, 102, Only(PointSnapKind.ApparentIntersection));

        Assert.Equal(PointSnapKind.ApparentIntersection, result!.Kind);
        Assert.Equal(300, result.Point.X, 2);
        Assert.Equal(100, result.Point.Y, 2);
    }

    [Fact]
    public void ApparentIntersection_IsNotUsedWhereTheLinesReallyCross()
    {
        var document = DocumentWith(Line(100, 100, 300, 300), Line(100, 300, 300, 100));

        Assert.Null(Snap(document, 203, 198, Only(PointSnapKind.ApparentIntersection)));
    }

    [Fact]
    public void Parallel_SnapsOnTheLineThatRunsAlongAnotherLine()
    {
        var document = DocumentWith(Line(200, 300, 400, 300));

        var result = Snap(document, 250, 103, Only(PointSnapKind.Parallel), new SKPoint(100, 100));

        Assert.Equal(PointSnapKind.Parallel, result!.Kind);
        Assert.Equal(250, result.Point.X, 2);
        Assert.Equal(100, result.Point.Y, 2);
    }

    [Fact]
    public void Insertion_SnapsToTheInsertionPointOfADoor()
    {
        var door = new Shape { Kind = ShapeKind.DoorSingle, X = 100, Y = 100, Width = 90, Height = 90 };
        ArchitecturalSymbols.ApplyDefaults(door);

        var result = Snap(DocumentWith(door), 102, 103, Only(PointSnapKind.Insertion));

        Assert.Equal(PointSnapKind.Insertion, result!.Kind);
        Assert.Equal(door.X, result.Point.X, 2);
        Assert.Equal(door.Y, result.Point.Y, 2);
    }

    [Fact]
    public void Node_SnapsToTheCentreLineCornerOfAWall()
    {
        var wall = new Shape { Kind = ShapeKind.Wall, X = 100, Y = 100, WallThickness = 16 };
        wall.Points = [new CorePoint(0, 0), new CorePoint(200, 0), new CorePoint(200, 200)];
        PathBoundsCalculator.Recompute(wall);

        var result = Snap(DocumentWith(wall), 301, 99, Only(PointSnapKind.Node));

        Assert.Equal(PointSnapKind.Node, result!.Kind);
        Assert.Equal(300, result.Point.X, 2);
        Assert.Equal(100, result.Point.Y, 2);
    }

    [Fact]
    public void Perpendicular_CanLandOnTheCentreLineOfAWall_SoAWallEndsInATJoint()
    {
        var wall = new Shape { Kind = ShapeKind.Wall, X = 200, Y = 100, WallThickness = 16 };
        wall.Points = [new CorePoint(0, 0), new CorePoint(0, 300)];
        PathBoundsCalculator.Recompute(wall);

        // a wall comes from the right at y = 250: its end near the opposite wall lands on the centre line (x = 200), not on a face
        var result = Snap(DocumentWith(wall), 203, 252, Only(PointSnapKind.Perpendicular), new SKPoint(400, 250));

        Assert.Equal(PointSnapKind.Perpendicular, result!.Kind);
        Assert.Equal(200, result.Point.X, 2);
        Assert.Equal(250, result.Point.Y, 2);
    }

    [Fact]
    public void Midpoint_SnapsToTheMiddleOfAWallsCentreLine()
    {
        var wall = new Shape { Kind = ShapeKind.Wall, X = 100, Y = 100, WallThickness = 16 };
        wall.Points = [new CorePoint(0, 0), new CorePoint(200, 0)];
        PathBoundsCalculator.Recompute(wall);

        var result = Snap(DocumentWith(wall), 202, 101, Only(PointSnapKind.Midpoint));

        Assert.Equal(PointSnapKind.Midpoint, result!.Kind);
        Assert.Equal(200, result.Point.X, 2);
        Assert.Equal(100, result.Point.Y, 2);
    }

    [Fact]
    public void ExcludedShapes_DoNotAttract()
    {
        var line = Line(100, 100, 200, 100);

        Assert.Null(Snap(DocumentWith(line), 203, 102, Only(PointSnapKind.Endpoint), null, line));
    }

    [Fact]
    public void WhenOff_NothingSnaps()
    {
        var options = Only(PointSnapKind.Endpoint);
        options.Enabled = false;

        Assert.Null(Snap(DocumentWith(Line(100, 100, 200, 100)), 203, 102, options));
    }

    [Fact]
    public void AnEndpointBeatsTheMiddle_WhenBothAreInReach()
    {
        var document = DocumentWith(Line(100, 100, 108, 100));

        // the middle (104) and the end (108) are both within reach of x = 106
        var result = Snap(document, 106.5f, 100, Only(PointSnapKind.Endpoint, PointSnapKind.Midpoint));

        Assert.Equal(PointSnapKind.Endpoint, result!.Kind);
    }

    [Fact]
    public void FixedPoints_ListTheCornersInsideAnArea()
    {
        var document = DocumentWith(Line(100, 100, 200, 100), Line(500, 500, 600, 500));

        var points = PointSnapper.FixedPoints(document, new SKRect(50, 50, 250, 150), Only(PointSnapKind.Endpoint, PointSnapKind.Midpoint), []);

        Assert.Equal(3, points.Count); // two ends and the middle of the first line
        Assert.DoesNotContain(points, p => p.Point.X > 300);
    }
}
