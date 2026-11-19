using Icono.Core;
using Icono.IO;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

[Collection("Scale")]
public class ArcSegmentTests
{
    private static Shape Polyline(IReadOnlyList<CorePoint> points, IReadOnlyList<double>? bulges, ShapeKind kind = ShapeKind.Polyline)
    {
        var shape = new Shape { Kind = kind, X = 0, Y = 0, Points = points, SegmentBulges = bulges };
        PathBoundsCalculator.Recompute(shape);
        return shape;
    }

    /// <summary>How near the path comes to a point (the path is sampled every pixel along its length).</summary>
    private static double Nearest(SKPath path, SKPoint target)
    {
        var best = double.MaxValue;
        using var measure = new SKPathMeasure(path, false);
        do
        {
            for (var at = 0f; at <= measure.Length; at += 0.5f)
            {
                if (measure.GetPosition(at, out var point))
                {
                    best = Math.Min(best, Math.Sqrt((point.X - target.X) * (point.X - target.X) + (point.Y - target.Y) * (point.Y - target.Y)));
                }
            }
        }
        while (measure.NextContour());

        return best;
    }

    [Fact]
    public void ThreePointsOnASemicircle_GiveABulgeOfOne()
    {
        var bulge = ArcSegment.FromThreePoints(new CorePoint(0, 0), new CorePoint(50, 50), new CorePoint(100, 0));

        Assert.Equal(1, bulge, 6);
        Assert.Equal(-1, ArcSegment.FromThreePoints(new CorePoint(0, 0), new CorePoint(50, -50), new CorePoint(100, 0)), 6);
    }

    [Fact]
    public void ThreePointsInALine_GiveAStraightSide()
    {
        Assert.Equal(0, ArcSegment.FromThreePoints(new CorePoint(0, 0), new CorePoint(40, 0), new CorePoint(100, 0)));
        Assert.Equal(0, ArcSegment.FromThreePoints(new CorePoint(0, 0), new CorePoint(0, 0), new CorePoint(100, 0)));
    }

    [Theory]
    [InlineData(50, 50)]   // a semicircle below the chord
    [InlineData(50, -20)]  // a flat arc above it
    [InlineData(30, 12)]   // not in the middle
    [InlineData(50, 120)]  // a point far out: more than a half circle
    [InlineData(-40, 60)]  // beyond the start: the long way round
    public void TheDrawnArc_PassesThroughTheThreePoints(double mx, double my)
    {
        CorePoint a = new(0, 0), m = new(mx, my), b = new(100, 0);
        var bulge = ArcSegment.FromThreePoints(a, m, b);
        var shape = Polyline([a, b], [bulge, 0]);

        using var path = ShapeGeometryFactory.Build(shape);

        Assert.True(Nearest(path, new SKPoint((float)m.X, (float)m.Y)) < 0.6, "the arc passes through the point it was made with");
        Assert.True(Nearest(path, new SKPoint(0, 0)) < 0.6);
        Assert.True(Nearest(path, new SKPoint(100, 0)) < 0.6);
    }

    [Fact]
    public void AllPointsOfTheArc_AreOneRadiusFromTheCentre()
    {
        CorePoint a = new(10, 20), m = new(60, 90), b = new(120, 40);
        var bulge = ArcSegment.FromThreePoints(a, m, b);
        var arc = ArcSegment.Describe(a, b, bulge)!.Value;

        for (var i = 0; i <= 10; i++)
        {
            var p = ArcSegment.PointAt(arc, i / 10.0);
            var distance = Math.Sqrt(Math.Pow(p.X - arc.Center.X, 2) + Math.Pow(p.Y - arc.Center.Y, 2));
            Assert.Equal(arc.Radius, distance, 6);
        }

        var first = ArcSegment.PointAt(arc, 0);
        var last = ArcSegment.PointAt(arc, 1);
        Assert.Equal(a.X, first.X, 6);
        Assert.Equal(a.Y, first.Y, 6);
        Assert.Equal(b.X, last.X, 6);
        Assert.Equal(b.Y, last.Y, 6);
    }

    [Fact]
    public void TheFrame_HoldsTheWholeArc()
    {
        var bulge = ArcSegment.FromThreePoints(new CorePoint(0, 0), new CorePoint(50, 50), new CorePoint(100, 0));
        var shape = Polyline([new CorePoint(0, 0), new CorePoint(100, 0)], [bulge, 0]);

        Assert.Equal(50, shape.Height, 3); // the semicircle dips 50 below its chord
        Assert.Equal(100, shape.Width, 3);

        // an arc that is more than a half circle reaches out beyond its two end points
        var wide = ArcSegment.FromThreePoints(new CorePoint(0, 0), new CorePoint(50, 100), new CorePoint(100, 0));
        var round = Polyline([new CorePoint(0, 0), new CorePoint(100, 0)], [wide, 0]);
        Assert.True(round.Width > 100, $"width {round.Width}");
    }

    [Fact]
    public void ASideThatIsStraight_DrawsAsBefore()
    {
        var with = Polyline([new CorePoint(0, 0), new CorePoint(50, 0), new CorePoint(50, 50)], [0, 0, 0]);
        var without = Polyline([new CorePoint(0, 0), new CorePoint(50, 0), new CorePoint(50, 50)], null);

        using var first = ShapeGeometryFactory.Build(with);
        using var second = ShapeGeometryFactory.Build(without);

        Assert.Equal(second.Bounds.Width, first.Bounds.Width, 3);
        Assert.Equal(second.Bounds.Height, first.Bounds.Height, 3);
    }

    [Fact]
    public void AClosedShape_CanHaveAnArcAsItsClosingSide()
    {
        // three points, the last side (back from the last point to the first) is an arc
        var polygon = Polyline([new CorePoint(0, 0), new CorePoint(100, 0), new CorePoint(100, 1)], [0, 0, ArcSegment.FromThreePoints(new CorePoint(100, 1), new CorePoint(50, 60), new CorePoint(0, 0))], ShapeKind.Polygon);
        using var path = ShapeGeometryFactory.Build(polygon);

        Assert.True(polygon.Height > 50, $"the arc closing the shape is in the frame: {polygon.Height}");
        Assert.True(Nearest(path, new SKPoint(50, 60)) < 0.6);
    }

    [Fact]
    public void SplittingAnArc_KeepsTheSameCircle()
    {
        CorePoint a = new(0, 0), b = new(100, 0);
        var bulge = ArcSegment.FromThreePoints(a, new CorePoint(50, 50), b);
        var arc = ArcSegment.Describe(a, b, bulge)!.Value;

        var middle = ArcSegment.PointAt(arc, 0.3);
        var (first, second) = ArcSegment.Split(bulge, 0.3);
        var shape = Polyline([a, middle, b], [first, second, 0]);
        using var path = ShapeGeometryFactory.Build(shape);

        // every point of the original arc is still on the drawn path
        for (var i = 0; i <= 10; i++)
        {
            var p = ArcSegment.PointAt(arc, i / 10.0);
            Assert.True(Nearest(path, new SKPoint((float)p.X, (float)p.Y)) < 0.6, $"at {i / 10.0}");
        }
    }

    [Fact]
    public void TheNearestPointOfAnArc_IsOnTheArc()
    {
        CorePoint a = new(0, 0), b = new(100, 0);
        var bulge = ArcSegment.FromThreePoints(a, new CorePoint(50, 50), b);

        var (point, distance, fraction) = ArcSegment.Nearest(a, b, bulge, 50, 80);

        Assert.Equal(50, point.X, 3);
        Assert.Equal(50, point.Y, 3);
        Assert.Equal(30, distance, 3);
        Assert.Equal(0.5, fraction, 3);
    }

    [Fact]
    public void TheDirectionAtTheEnds_FollowsTheArc()
    {
        // a semicircle that dips below the chord: it leaves downwards and arrives going up
        var bulge = ArcSegment.FromThreePoints(new CorePoint(0, 0), new CorePoint(50, 50), new CorePoint(100, 0));

        var (start, end) = ArcSegment.Tangents(new CorePoint(0, 0), new CorePoint(100, 0), bulge);

        Assert.Equal(0, start.X, 6);
        Assert.Equal(1, start.Y, 6);
        Assert.Equal(0, end.X, 6);
        Assert.Equal(-1, end.Y, 6);
    }

    [Fact]
    public void TheBulges_AreKeptInTheFile_AndByUndo()
    {
        var bulge = ArcSegment.FromThreePoints(new CorePoint(0, 0), new CorePoint(50, 50), new CorePoint(100, 0));
        var shape = Polyline([new CorePoint(0, 0), new CorePoint(100, 0)], [bulge, 0]);
        var document = new Document { Width = 300, Height = 200 };
        var layer = new Layer { Name = "L" };
        layer.Shapes.Add(shape);
        document.Layers.Add(layer);

        var path = Path.Combine(Path.GetTempPath(), $"icono-arc-{Guid.NewGuid():N}{DocumentFile.Extension}");
        try
        {
            DocumentFile.Save(document, path);
            var loaded = new Document();
            DocumentFile.Load(path).ApplyTo(loaded);

            var back = Assert.Single(Assert.Single(loaded.Layers).Shapes);
            Assert.Equal([bulge, 0], back.SegmentBulges);
        }
        finally
        {
            File.Delete(path);
        }

        var snapshot = ShapeSnapshot.Capture(shape);
        shape.SegmentBulges = null;
        snapshot.ApplyTo(shape);
        Assert.Equal([bulge, 0], shape.SegmentBulges);
    }
}

[Collection("Scale")]
public class ArcSegmentEditingTests
{
    private static Shape ArcLine()
    {
        var bulge = ArcSegment.FromThreePoints(new CorePoint(0, 0), new CorePoint(50, 50), new CorePoint(100, 0));
        var shape = new Shape { Kind = ShapeKind.Polyline, Points = [new CorePoint(0, 0), new CorePoint(100, 0), new CorePoint(100, 80)], SegmentBulges = [bulge, 0, 0] };
        PathBoundsCalculator.Recompute(shape);
        return shape;
    }

    [Fact]
    public void APointOnAnArc_DividesItIntoTwoArcsOfTheSameCircle()
    {
        var shape = ArcLine();
        var context = new Icono.Editor.EditorContext();
        var onArc = new CorePoint(50 + shape.X, 50 + shape.Y); // the middle of the arc

        Assert.True(Icono.Editor.PointEditing.InsertPointNear(context, shape, onArc.X, onArc.Y, 6));

        Assert.Equal(4, shape.Points!.Count);
        Assert.Equal(4, shape.SegmentBulges!.Count);
        Assert.True(ArcSegment.IsArc(shape.SegmentBulges[0]));
        Assert.True(ArcSegment.IsArc(shape.SegmentBulges[1]));
        Assert.Equal(shape.SegmentBulges[0], shape.SegmentBulges[1], 6); // the middle: two equal halves
        Assert.Equal(0, shape.SegmentBulges[2]);
    }

    [Fact]
    public void TakingAPointOut_MakesTheSideNextToItStraight()
    {
        var shape = ArcLine();
        var context = new Icono.Editor.EditorContext();

        Assert.True(Icono.Editor.PointEditing.RemovePointNear(context, shape, shape.X + 100, shape.Y, 6)); // the second point

        Assert.Equal(2, shape.Points!.Count);
        Assert.Null(shape.SegmentBulges); // the arc led to the point that went: no arc is left
    }

    [Fact]
    public void ClosingAPolyline_KeepsTheArcOfItsLastSide()
    {
        var bulge = ArcSegment.FromThreePoints(new CorePoint(100, 80), new CorePoint(40, 120), new CorePoint(0, 0));
        var shape = new Shape
        {
            Kind = ShapeKind.Polyline,
            Points = [new CorePoint(0, 0), new CorePoint(100, 0), new CorePoint(100, 80), new CorePoint(0, 0)],
            SegmentBulges = [0, 0, bulge, 0],
        };
        PathBoundsCalculator.Recompute(shape);

        Icono.Editor.PolylineClosing.Close(shape);

        Assert.Equal(ShapeKind.Polygon, shape.Kind);
        Assert.Equal(3, shape.Points!.Count);
        Assert.Equal([0, 0, bulge], shape.SegmentBulges);
    }
}
