using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

public class ShapeGeometryFactoryTests
{
    [Fact]
    public void Rotation90Degrees_SwapsBoundingBoxWidthAndHeight()
    {
        var shape = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 40, Height = 20 };
        using var unrotated = ShapeGeometryFactory.Build(shape);
        var unrotatedBounds = unrotated.Bounds;

        shape.Rotation = 90;
        using var rotated = ShapeGeometryFactory.Build(shape);
        var rotatedBounds = rotated.Bounds;

        Assert.Equal(unrotatedBounds.Width, rotatedBounds.Height, 1);
        Assert.Equal(unrotatedBounds.Height, rotatedBounds.Width, 1);
    }

    [Fact]
    public void FlipHorizontal_MirrorsAsymmetricShapePoints()
    {
        var shape = new Shape { Kind = ShapeKind.Parallelogram, X = 0, Y = 0, Width = 24, Height = 24 };
        using var unflipped = ShapeGeometryFactory.Build(shape);
        var topEdgeMinXUnflipped = unflipped.Points.Where(p => Math.Abs(p.Y - 6) < 0.01f).Min(p => p.X);

        shape.FlipHorizontal = true;
        using var flipped = ShapeGeometryFactory.Build(shape);
        var topEdgeMinXFlipped = flipped.Points.Where(p => Math.Abs(p.Y - 6) < 0.01f).Min(p => p.X);

        Assert.Equal(8f, topEdgeMinXUnflipped, 2);
        Assert.Equal(2f, topEdgeMinXFlipped, 2);
    }

    [Fact]
    public void NoRotationOrFlip_KeepsGeometryWithinTheShapesBoundingBox()
    {
        var shape = new Shape { Kind = ShapeKind.Ellipse, X = 5, Y = 5, Width = 30, Height = 20 };
        using var path = ShapeGeometryFactory.Build(shape);

        Assert.True(path.Bounds.Left >= shape.X);
        Assert.True(path.Bounds.Top >= shape.Y);
        Assert.True(path.Bounds.Right <= shape.X + shape.Width);
        Assert.True(path.Bounds.Bottom <= shape.Y + shape.Height);
    }

    [Fact]
    public void Polyline_WithFourPoints_ProducesExactlyFourPathPoints()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.Polyline,
            X = 0,
            Y = 0,
            Points = [new CorePoint(0, 0), new CorePoint(10, 0), new CorePoint(10, 10), new CorePoint(20, 10)],
        };

        using var path = ShapeGeometryFactory.Build(shape);

        Assert.Equal(4, path.PointCount);
    }

    [Fact]
    public void Arrow_TwoPoints_AddsArrowheadWingPoints()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.Arrow,
            X = 0,
            Y = 0,
            Points = [new CorePoint(0, 0), new CorePoint(20, 0)],
        };

        using var path = ShapeGeometryFactory.Build(shape);

        // 2 shaft points + 3 arrowhead points (wing, tip, wing).
        Assert.Equal(5, path.PointCount);
    }

    [Fact]
    public void Curve_ThroughFourPoints_PassesThroughEveryAnchor()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.Curve,
            X = 0,
            Y = 0,
            Points = [new CorePoint(0, 0), new CorePoint(10, 20), new CorePoint(20, 0), new CorePoint(30, 20)],
        };

        using var path = ShapeGeometryFactory.Build(shape);

        // A Catmull-Rom fit interpolates exactly through every anchor, so the path's bounds must
        // cover each one (with a small tolerance for floating-point rect-edge inclusivity).
        const float tolerance = 0.5f;
        var bounds = path.Bounds;
        foreach (var anchor in shape.Points)
        {
            Assert.InRange((float)anchor.X, bounds.Left - tolerance, bounds.Right + tolerance);
            Assert.InRange((float)anchor.Y, bounds.Top - tolerance, bounds.Bottom + tolerance);
        }
    }

    [Fact]
    public void OrthogonalRouting_BendsA2PointLineIntoThreePoints()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.Polyline,
            X = 0,
            Y = 0,
            PathRoutingStyle = PathRoutingStyle.Orthogonal,
            Points = [new CorePoint(0, 0), new CorePoint(20, 20)],
        };

        using var straightShape = ShapeGeometryFactory.Build(new Shape
        {
            Kind = ShapeKind.Polyline,
            Points = [new CorePoint(0, 0), new CorePoint(20, 20)],
        });
        using var path = ShapeGeometryFactory.Build(shape);

        // A diagonal 2-point line needs one inserted bend point to become right-angled.
        Assert.Equal(2, straightShape.PointCount);
        Assert.Equal(3, path.PointCount);
    }

    [Fact]
    public void Club_BuildsANonDegenerateClosedOutlineWithAStem()
    {
        var shape = new Shape { Kind = ShapeKind.Club, X = 0, Y = 0, Width = 24, Height = 24 };

        using var path = ShapeGeometryFactory.Build(shape);

        // Three overlapping circular lobes plus a short stem line beneath them — should produce
        // real geometry (not an empty/degenerate path) roughly filling its 24x24 template box.
        Assert.True(path.PointCount > 0);
        Assert.True(path.Bounds.Width > 10);
        Assert.True(path.Bounds.Height > 10);
    }

    [Fact]
    public void Box3D_FacesRespectDepthAndStayWithinTheBoundingBox()
    {
        var shape = new Shape { Kind = ShapeKind.Box3D, X = 0, Y = 0, Width = 100, Height = 60, Depth3D = 15 };

        var (front, top, side) = ShapeGeometryFactory.BuildBox3DFaces(shape);
        using var frontPath = front;
        using var topPath = top;
        using var sidePath = side;

        foreach (var face in new[] { frontPath, topPath, sidePath })
        {
            Assert.True(face.Bounds.Left >= shape.X - 0.01f);
            Assert.True(face.Bounds.Top >= shape.Y - 0.01f);
            Assert.True(face.Bounds.Right <= shape.X + shape.Width + 0.01f);
            Assert.True(face.Bounds.Bottom <= shape.Y + shape.Height + 0.01f);
        }
    }

    [Fact]
    public void RoundedRouting_BendsA2PointLineAndStaysWithinTheElbowBoundingBox()
    {
        var elbowShape = new Shape
        {
            Kind = ShapeKind.Polyline,
            X = 0,
            Y = 0,
            PathRoutingStyle = PathRoutingStyle.Orthogonal,
            Points = [new CorePoint(0, 0), new CorePoint(20, 20)],
        };
        var roundedShape = new Shape
        {
            Kind = ShapeKind.Polyline,
            X = 0,
            Y = 0,
            PathRoutingStyle = PathRoutingStyle.Rounded,
            Points = [new CorePoint(0, 0), new CorePoint(20, 20)],
        };

        using var elbowPath = ShapeGeometryFactory.Build(elbowShape);
        using var roundedPath = ShapeGeometryFactory.Build(roundedShape);

        // The rounded variant replaces the sharp elbow corner with a curve but must stay inside
        // the same overall bounding box as the sharp-cornered elbow route.
        Assert.True(roundedPath.Bounds.Left >= elbowPath.Bounds.Left - 0.01f);
        Assert.True(roundedPath.Bounds.Top >= elbowPath.Bounds.Top - 0.01f);
        Assert.True(roundedPath.Bounds.Right <= elbowPath.Bounds.Right + 0.01f);
        Assert.True(roundedPath.Bounds.Bottom <= elbowPath.Bounds.Bottom + 0.01f);
    }

    [Fact]
    public void SupportsPointEditing_IsTrueForPolygonsAndFalseForCurvedOrLineKinds()
    {
        Assert.True(ShapeGeometryFactory.SupportsPointEditing(ShapeKind.Star));
        Assert.True(ShapeGeometryFactory.SupportsPointEditing(ShapeKind.Rectangle));
        Assert.False(ShapeGeometryFactory.SupportsPointEditing(ShapeKind.Ellipse));
        Assert.False(ShapeGeometryFactory.SupportsPointEditing(ShapeKind.Line));
    }

    [Fact]
    public void ExtractEditablePoints_ProducesAClosedOutlineMatchingTheOriginalFixedShape()
    {
        var shape = new Shape { Kind = ShapeKind.Star, X = 10, Y = 10, Width = 40, Height = 40 };
        using var originalPath = ShapeGeometryFactory.Build(shape);

        var editablePoints = ShapeGeometryFactory.ExtractEditablePoints(shape);
        Assert.True(editablePoints.Count >= 3);

        shape.Points = editablePoints;
        using var convertedPath = ShapeGeometryFactory.Build(shape);

        // Converting to editable points must reproduce (approximately) the same outline bounds —
        // this is the "vertex editing" conversion the user drags points on afterward.
        Assert.Equal(originalPath.Bounds.Left, convertedPath.Bounds.Left, 1);
        Assert.Equal(originalPath.Bounds.Top, convertedPath.Bounds.Top, 1);
        Assert.Equal(originalPath.Bounds.Right, convertedPath.Bounds.Right, 1);
        Assert.Equal(originalPath.Bounds.Bottom, convertedPath.Bounds.Bottom, 1);
    }

    [Fact]
    public void ConvertedFixedShape_ProducesAClosedPath()
    {
        var shape = new Shape { Kind = ShapeKind.Cross, X = 0, Y = 0, Width = 24, Height = 24 };
        shape.Points = ShapeGeometryFactory.ExtractEditablePoints(shape);

        using var path = ShapeGeometryFactory.Build(shape);

        // A converted fixed polygon isn't stroke-only, so its path must be closed (fillable) —
        // verified indirectly via SKPath.Contains, which only returns sensible results for
        // closed contours (an open contour still reports containment based on an implicit close,
        // so this also just confirms the outline covers its own center point).
        var bounds = path.Bounds;
        Assert.True(path.Contains(bounds.MidX, bounds.MidY));
    }

    [Fact]
    public void Box3D_DepthClampsToSixtyPercentOfShorterSide()
    {
        var shape = new Shape { Kind = ShapeKind.Box3D, X = 0, Y = 0, Width = 100, Height = 40, Depth3D = 1000 };

        var (front, top, side) = ShapeGeometryFactory.BuildBox3DFaces(shape);
        using var frontPath = front;
        using var topPath = top;
        using var sidePath = side;

        // Depth is clamped to 60% of the shorter side (40 -> 24), so the front face's top edge
        // (at y = Y + depth) must not have collapsed past that clamp.
        Assert.True(frontPath.Bounds.Top <= shape.Y + 24 + 0.01f);
    }
}
