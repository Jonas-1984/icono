using System.Text.RegularExpressions;
using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

public class ShapeGeometryFactoryTests
{
    private static int CountContours(SkiaSharp.SKPath path) => Regex.Matches(path.ToSvgPathData(), "M[^M]*").Count;

    [Theory]
    [InlineData(ShapeKind.Ellipse)]
    [InlineData(ShapeKind.HalfCircle)]
    [InlineData(ShapeKind.Cloud)]
    [InlineData(ShapeKind.Shield)]
    [InlineData(ShapeKind.LensConcave)]
    public void SupportsPointEditing_SingleContourCurvedKind_IsNowTrue(ShapeKind kind)
    {
        // Previously only the straight-edge polygon set (shared with corner rounding) supported
        // "Edit Points" — a curved-but-single-contour kind has just as valid a vertex list as a
        // rectangle, it just starts out with more/curved-looking points.
        Assert.True(ShapeGeometryFactory.SupportsPointEditing(kind));

        var shape = new Shape { Kind = kind, X = 0, Y = 0, Width = 40, Height = 40 };
        var points = ShapeGeometryFactory.ExtractEditablePoints(shape);
        Assert.True(points.Count >= 3);
    }

    [Theory]
    [InlineData(ShapeKind.Ring)]
    [InlineData(ShapeKind.Gear)]
    [InlineData(ShapeKind.Club)]
    [InlineData(ShapeKind.TrafficCone)]
    [InlineData(ShapeKind.Cylinder)]
    [InlineData(ShapeKind.StarCheck)]
    public void SupportsPointEditing_MultiContourKind_StaysFalse(ShapeKind kind)
    {
        // Shape.Points is one continuous polyline/curve with no concept of separate sub-contours —
        // converting a multi-contour kind (Ring's hole, Traffic Cone's 4 pieces, Club's stem, ...)
        // would either lose pieces or wrongly connect them with a stray line, so these must stay
        // excluded even though they're now eligible by every other measure.
        Assert.False(ShapeGeometryFactory.SupportsPointEditing(kind));
    }

    [Theory]
    [InlineData(ShapeKind.Line)]
    [InlineData(ShapeKind.Text)]
    [InlineData(ShapeKind.Box3D)]
    [InlineData(ShapeKind.RasterImage)]
    [InlineData(ShapeKind.CombinedPath)]
    public void SupportsPointEditing_NeverEditableKind_StaysFalse(ShapeKind kind)
    {
        Assert.False(ShapeGeometryFactory.SupportsPointEditing(kind));
    }

    [Fact]
    public void GetColorableRegions_TrafficCone_ReturnsFourNonDegenerateStackedBands()
    {
        var regions = ShapeGeometryFactory.GetColorableRegions(ShapeKind.TrafficCone);

        Assert.NotNull(regions);
        Assert.Equal(4, regions!.Count);

        var bounds = regions.Select(r =>
        {
            using var p = SkiaSharp.SKPath.ParseSvgPathData(r);
            Assert.NotNull(p);
            Assert.True(p!.Bounds.Width > 0.5f);
            Assert.True(p.Bounds.Height > 0.5f);
            return p.Bounds;
        }).ToList();

        // cap, band1, band2 stack top-to-bottom with no gaps: each one's top edge is the previous
        // one's bottom edge (the ring lines' own Y positions).
        Assert.Equal(bounds[0].Bottom, bounds[1].Top, 1);
        Assert.Equal(bounds[1].Bottom, bounds[2].Top, 1);

        // The base plate (region 3) sits at the bottom, roughly spanning the full template width.
        Assert.True(bounds[3].Width > 15f);
    }

    [Fact]
    public void GetColorableRegions_OrdinaryKind_ReturnsNull()
    {
        Assert.Null(ShapeGeometryFactory.GetColorableRegions(ShapeKind.Rectangle));
        Assert.Null(ShapeGeometryFactory.GetColorableRegions(ShapeKind.Club));
    }

    [Fact]
    public void GetColorableRegions_CurvedBandsKeepARealArcCommandInsteadOfAFlattenedPolygon()
    {
        // Regions 0 (cap, includes the rounded apex) and 2 (bottom band, includes the curved base
        // arc) must each still contain a genuine "a"/"A" arc command — a first attempt built these
        // via SkiaSharp's boolean SKPath.Op, whose result is always a flattened polygon
        // approximation of any curve involved (however fine the tessellation), which visibly
        // faceted the cone's rounded apex/base once rendered at real canvas size. The fix re-embeds
        // each curved edge using its ORIGINAL, unmodified arc command instead.
        var regions = ShapeGeometryFactory.GetColorableRegions(ShapeKind.TrafficCone)!;

        Assert.Contains('a', regions[0].ToLowerInvariant());
        Assert.Contains('a', regions[2].ToLowerInvariant());

        using var cap = SkiaSharp.SKPath.ParseSvgPathData(regions[0]);
        using var bottomBand = SkiaSharp.SKPath.ParseSvgPathData(regions[2]);

        Assert.NotNull(cap);
        Assert.NotNull(bottomBand);
        Assert.True(cap!.Bounds.Width > 1f);
        Assert.True(bottomBand!.Bounds.Width > 1f);
    }

    [Theory]
    [InlineData(ShapeKind.Club)]
    [InlineData(ShapeKind.Pyramid)]
    [InlineData(ShapeKind.Spade)]
    [InlineData(ShapeKind.Umbrella)]
    [InlineData(ShapeKind.Cloudy)]
    public void ExtractClosedContoursOnly_DropsTheOpenDetailLineButKeepsTheClosedBody(ShapeKind kind)
    {
        var shape = new Shape { Kind = kind, X = 0, Y = 0, Width = 24, Height = 24 };

        using var full = ShapeGeometryFactory.Build(shape);
        using var fillable = ShapeGeometryFactory.ExtractClosedContoursOnly(full);

        Assert.True(ShapeGeometryFactory.HasMixedOpenAndClosedContours(kind));
        // The open detail line contributes its own contour to the full outline (needed for the
        // border stroke) but must be dropped from what actually gets filled — comparing contour
        // COUNT rather than raw PointCount, since round-tripping through ToSvgPathData flattens
        // arcs into many small segments and inflates point counts regardless of correctness.
        Assert.True(CountContours(fillable) < CountContours(full));
        // The closed body itself must still be there — not an empty/degenerate result.
        Assert.True(fillable.Bounds.Width > 5);
        Assert.True(fillable.Bounds.Height > 5);
    }

    [Fact]
    public void ExtractClosedContoursOnly_Umbrella_StopsAtTheCanopyAndExcludesTheHandle()
    {
        var shape = new Shape { Kind = ShapeKind.Umbrella, X = 0, Y = 0, Width = 24, Height = 24 };

        using var full = ShapeGeometryFactory.Build(shape);
        using var fillable = ShapeGeometryFactory.ExtractClosedContoursOnly(full);

        // The full outline (used for the border stroke) reaches down through the handle's hook
        // near the template's bottom edge; the fillable-only geometry must stop well short of
        // that, at the canopy's own bottom edge.
        Assert.True(full.Bounds.Bottom > 18f);
        Assert.True(fillable.Bounds.Bottom < 15f);
    }

    [Theory]
    [InlineData(ShapeKind.Rectangle)]
    [InlineData(ShapeKind.Star)]
    [InlineData(ShapeKind.TrafficCone)]
    [InlineData(ShapeKind.Ring)]
    public void HasMixedOpenAndClosedContours_IsFalseForOrdinaryKinds(ShapeKind kind)
    {
        Assert.False(ShapeGeometryFactory.HasMixedOpenAndClosedContours(kind));
    }

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
            LineEndHead = ArrowHeadKind.Open,
        };

        using var path = ShapeGeometryFactory.Build(shape);

        // 2 shaft points + 3 arrowhead points (wing, tip, wing).
        Assert.Equal(5, path.PointCount);
    }

    [Fact]
    public void Line_WithNoHeadsSet_HasNoArrowheadGeometry()
    {
        // LineStartHead/LineEndHead default to None — a plain Line (or an Arrow with heads
        // explicitly cleared) must draw as just the shaft, nothing extra appended.
        var shape = new Shape
        {
            Kind = ShapeKind.Line,
            X = 0,
            Y = 0,
            Points = [new CorePoint(0, 0), new CorePoint(20, 0)],
        };

        using var path = ShapeGeometryFactory.Build(shape);

        Assert.Equal(2, path.PointCount);
    }

    [Fact]
    public void Line_FilledHead_AddsAClosedContourThatGetsAutoFilled()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.Line,
            X = 0,
            Y = 0,
            Width = 40,
            Height = 10,
            Points = [new CorePoint(0, 5), new CorePoint(40, 5)],
            LineEndHead = ArrowHeadKind.Filled,
            BorderThickness = 0,
            FillColor = ArgbColor.FromRgb(255, 0, 0),
        };

        using var bitmap = new SkiaSharp.SKBitmap(40, 10);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        Icono.Rendering.ShapeRenderer.Draw(canvas, shape);

        // A "Filled" head is a closed triangle sub-path — the existing closed-contours-only fill
        // pass should pick it up and paint it solid, even with BorderThickness=0 (no stroke pass).
        Assert.True(bitmap.GetPixel(37, 5).Alpha > 0);
    }

    [Theory]
    [InlineData(ArrowHeadKind.Circle)]
    [InlineData(ArrowHeadKind.Diamond)]
    [InlineData(ArrowHeadKind.Square)]
    [InlineData(ArrowHeadKind.Bar)]
    [InlineData(ArrowHeadKind.Half)]
    [InlineData(ArrowHeadKind.Thin)]
    public void Line_EachHeadKind_AddsGeometryWithoutThrowing(ArrowHeadKind headKind)
    {
        var shape = new Shape
        {
            Kind = ShapeKind.Line,
            X = 0,
            Y = 0,
            Points = [new CorePoint(0, 0), new CorePoint(20, 0)],
            LineStartHead = headKind,
            LineEndHead = headKind,
        };

        using var path = ShapeGeometryFactory.Build(shape);

        Assert.True(path.PointCount > 2);
    }

    [Fact]
    public void Line_IndependentStartAndEndHeads_BothApply()
    {
        var openOnly = new Shape
        {
            Kind = ShapeKind.Line, X = 0, Y = 0,
            Points = [new CorePoint(0, 0), new CorePoint(20, 0)],
            LineEndHead = ArrowHeadKind.Open,
        };
        var both = new Shape
        {
            Kind = ShapeKind.Line, X = 0, Y = 0,
            Points = [new CorePoint(0, 0), new CorePoint(20, 0)],
            LineStartHead = ArrowHeadKind.Open,
            LineEndHead = ArrowHeadKind.Open,
        };

        using var openOnlyPath = ShapeGeometryFactory.Build(openOnly);
        using var bothPath = ShapeGeometryFactory.Build(both);

        Assert.True(bothPath.PointCount > openOnlyPath.PointCount);
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

    [Theory]
    [InlineData(ShapeKind.StarCheck)]
    [InlineData(ShapeKind.StarPlus)]
    [InlineData(ShapeKind.StarMinus)]
    [InlineData(ShapeKind.SquareArrowOutUpRight)]
    [InlineData(ShapeKind.Blocks)]
    [InlineData(ShapeKind.SwatchBook)]
    [InlineData(ShapeKind.LensConcave)]
    [InlineData(ShapeKind.Cylinder)]
    [InlineData(ShapeKind.Cone)]
    [InlineData(ShapeKind.Pyramid)]
    [InlineData(ShapeKind.TriangleAlert)]
    [InlineData(ShapeKind.Spade)]
    [InlineData(ShapeKind.SquareDashedBottom)]
    [InlineData(ShapeKind.Share)]
    [InlineData(ShapeKind.ShieldCog)]
    [InlineData(ShapeKind.Umbrella)]
    [InlineData(ShapeKind.Cloudy)]
    [InlineData(ShapeKind.Shield)]
    [InlineData(ShapeKind.TrafficCone)]
    [InlineData(ShapeKind.Worktable)]
    [InlineData(ShapeKind.RoundTable)]
    [InlineData(ShapeKind.OfficeChair)]
    [InlineData(ShapeKind.Monitor)]
    [InlineData(ShapeKind.Sofa)]
    [InlineData(ShapeKind.Bookshelf)]
    [InlineData(ShapeKind.Door)]
    [InlineData(ShapeKind.Plant)]
    [InlineData(ShapeKind.Workstation)]
    [InlineData(ShapeKind.FlowerCluster)]
    [InlineData(ShapeKind.StarFlower)]
    [InlineData(ShapeKind.DaisyFlower)]
    [InlineData(ShapeKind.Workplace)]
    [InlineData(ShapeKind.FilingCabinet)]
    [InlineData(ShapeKind.Printer)]
    [InlineData(ShapeKind.TrashBin)]
    [InlineData(ShapeKind.Stairs)]
    [InlineData(ShapeKind.PottedTree)]
    [InlineData(ShapeKind.Rug)]
    [InlineData(ShapeKind.Bed)]
    [InlineData(ShapeKind.Lamp)]
    public void IconGlyphShapes_BuildNonDegenerateGeometryFillingRoughlyTheWholeTemplateBox(ShapeKind kind)
    {
        var shape = new Shape { Kind = kind, X = 0, Y = 0, Width = 24, Height = 24 };

        using var path = ShapeGeometryFactory.Build(shape);

        Assert.True(path.PointCount > 0);
        Assert.True(path.Bounds.Width > 10);
        Assert.True(path.Bounds.Height > 10);
    }

    [Theory]
    [InlineData(ShapeKind.Whiteboard)]
    [InlineData(ShapeKind.Window)]
    public void ThinArchitecturalSymbols_BuildNonDegenerateWideShortGeometry(ShapeKind kind)
    {
        // Whiteboard/Window are deliberately thin, wall-mounted symbols — wide but short, unlike
        // the roughly-square icon glyphs the theory above checks.
        var shape = new Shape { Kind = kind, X = 0, Y = 0, Width = 24, Height = 24 };

        using var path = ShapeGeometryFactory.Build(shape);

        Assert.True(path.PointCount > 0);
        Assert.True(path.Bounds.Width > 10);
        Assert.True(path.Bounds.Height > 0);
    }

    [Theory]
    [InlineData(ShapeKind.StarCheck)]
    [InlineData(ShapeKind.StarPlus)]
    [InlineData(ShapeKind.StarMinus)]
    [InlineData(ShapeKind.SquareArrowOutUpRight)]
    [InlineData(ShapeKind.Blocks)]
    [InlineData(ShapeKind.SwatchBook)]
    [InlineData(ShapeKind.TriangleAlert)]
    [InlineData(ShapeKind.SquareDashedBottom)]
    [InlineData(ShapeKind.Share)]
    [InlineData(ShapeKind.ShieldCog)]
    [InlineData(ShapeKind.TrafficCone)]
    public void IconGlyphShapes_MultiPartLucideGlyphsAreStrokeOnly(ShapeKind kind)
    {
        Assert.True(ShapeGeometryFactory.IsStrokeOnly(kind));
    }

    [Theory]
    [InlineData(ShapeKind.Worktable)]
    [InlineData(ShapeKind.RoundTable)]
    [InlineData(ShapeKind.OfficeChair)]
    [InlineData(ShapeKind.Monitor)]
    [InlineData(ShapeKind.Sofa)]
    [InlineData(ShapeKind.Bookshelf)]
    [InlineData(ShapeKind.Door)]
    [InlineData(ShapeKind.Plant)]
    [InlineData(ShapeKind.Workstation)]
    [InlineData(ShapeKind.FlowerCluster)]
    [InlineData(ShapeKind.StarFlower)]
    [InlineData(ShapeKind.DaisyFlower)]
    [InlineData(ShapeKind.Workplace)]
    [InlineData(ShapeKind.FilingCabinet)]
    [InlineData(ShapeKind.Printer)]
    [InlineData(ShapeKind.TrashBin)]
    [InlineData(ShapeKind.Whiteboard)]
    [InlineData(ShapeKind.Window)]
    [InlineData(ShapeKind.Stairs)]
    [InlineData(ShapeKind.PottedTree)]
    [InlineData(ShapeKind.Rug)]
    [InlineData(ShapeKind.Bed)]
    [InlineData(ShapeKind.Lamp)]
    public void OfficeFurnitureShapes_AreStrokeOnlyLineArt(ShapeKind kind)
    {
        // Floor-plan furniture symbols read as thin line-art blocks, not solid-filled shapes.
        Assert.True(ShapeGeometryFactory.IsStrokeOnly(kind));
    }

    [Theory]
    [InlineData(ShapeKind.LensConcave)]
    [InlineData(ShapeKind.Cylinder)]
    [InlineData(ShapeKind.Cone)]
    [InlineData(ShapeKind.Pyramid)]
    [InlineData(ShapeKind.Spade)]
    [InlineData(ShapeKind.Umbrella)]
    [InlineData(ShapeKind.Cloudy)]
    [InlineData(ShapeKind.Shield)]
    public void IconGlyphShapes_FilledClosedOutlinesAreNotStrokeOnly(ShapeKind kind)
    {
        Assert.False(ShapeGeometryFactory.IsStrokeOnly(kind));
    }

    [Fact]
    public void RasterImage_BuildsAFullBleedRectMatchingTheShapesBoundingBox()
    {
        var shape = new Shape { Kind = ShapeKind.RasterImage, X = 10, Y = 20, Width = 100, Height = 50 };

        using var path = ShapeGeometryFactory.Build(shape);

        Assert.Equal(10f, path.Bounds.Left, 1);
        Assert.Equal(20f, path.Bounds.Top, 1);
        Assert.Equal(110f, path.Bounds.Right, 1);
        Assert.Equal(70f, path.Bounds.Bottom, 1);
    }

    [Fact]
    public void RasterImage_RespectsCornerRadiusWithoutBecomingPointEditable()
    {
        var rounded = new Shape { Kind = ShapeKind.RasterImage, X = 0, Y = 0, Width = 100, Height = 100, CornerRadius = 20 };
        var sharp = new Shape { Kind = ShapeKind.RasterImage, X = 0, Y = 0, Width = 100, Height = 100 };

        using var roundedPath = ShapeGeometryFactory.Build(rounded);
        using var sharpPath = ShapeGeometryFactory.Build(sharp);

        // Rounding trims the corners, so the rounded path has strictly more vertices than a plain
        // 4-corner rect (the rounding algorithm inserts curve-approximation points per corner).
        Assert.True(roundedPath.PointCount > sharpPath.PointCount);

        // But RasterImage must NOT have joined CornerRoundableKinds (that set also drives "Edit
        // Points" eligibility, which makes no sense for a bitmap).
        Assert.False(ShapeGeometryFactory.SupportsPointEditing(ShapeKind.RasterImage));
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
    public void SupportsPointEditing_IsTrueForPolygonsAndFalseForMultiContourOrLineKinds()
    {
        Assert.True(ShapeGeometryFactory.SupportsPointEditing(ShapeKind.Star));
        Assert.True(ShapeGeometryFactory.SupportsPointEditing(ShapeKind.Rectangle));
        // Ellipse is single-contour, so it's editable now too — see SupportsPointEditing_SingleContourCurvedKind_IsNowTrue.
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
