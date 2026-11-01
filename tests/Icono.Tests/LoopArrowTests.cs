using Icono.Core;
using Icono.IO;
using Icono.Rendering;

namespace Icono.Tests;

/// <summary>The loop arrows carry the line ends of the shape like an Arrow does, and the head can be chosen.</summary>
public class LoopArrowTests
{
    private static Shape Loop(ShapeKind kind, ArrowHeadKind start, ArrowHeadKind end) =>
        new() { Kind = kind, X = 100, Y = 100, Width = 120, Height = 120, LineStartHead = start, LineEndHead = end };

    [Theory]
    [InlineData(ShapeKind.LoopArrowClockwise)]
    [InlineData(ShapeKind.LoopArrowCounterClockwise)]
    public void TheHeadComesFromTheLineEnds_NoneDrawsJustTheArc(ShapeKind kind)
    {
        using var plain = ShapeGeometryFactory.Build(Loop(kind, ArrowHeadKind.None, ArrowHeadKind.None));
        using var open = ShapeGeometryFactory.Build(Loop(kind, ArrowHeadKind.None, ArrowHeadKind.Open));
        using var filled = ShapeGeometryFactory.Build(Loop(kind, ArrowHeadKind.None, ArrowHeadKind.Filled));
        using var both = ShapeGeometryFactory.Build(Loop(kind, ArrowHeadKind.Open, ArrowHeadKind.Open));

        Assert.True(open.PointCount > plain.PointCount);
        Assert.True(both.PointCount > open.PointCount);
        Assert.NotEqual(open.ToSvgPathData(), filled.ToSvgPathData());
        Assert.True(ShapeGeometryFactory.IsLoopArrow(kind));
    }

    [Fact]
    public void TheHead_SitsOnTheEndOfTheArc()
    {
        using var plain = ShapeGeometryFactory.Build(Loop(ShapeKind.LoopArrowClockwise, ArrowHeadKind.None, ArrowHeadKind.None));
        using var headed = ShapeGeometryFactory.Build(Loop(ShapeKind.LoopArrowClockwise, ArrowHeadKind.None, ArrowHeadKind.Filled));

        // the head is a few px of wings round the end of the arc: the whole outline hardly grows
        Assert.InRange(headed.Bounds.Width - plain.Bounds.Width, -1, 12);
        Assert.InRange(headed.Bounds.Height - plain.Bounds.Height, -1, 12);
    }

    [Fact]
    public void FilesOfTheFirstVersion_GiveTheirLoopsTheOpenHeadTheyHadBefore_NewFilesKeepWhatWasChosen()
    {
        var old = new DocumentFile { Version = 1, Width = 100, Height = 100 };
        old.Layers.Add(new LayerFile { Name = "L", Shapes = [Loop(ShapeKind.LoopArrowClockwise, ArrowHeadKind.None, ArrowHeadKind.None)] });
        var document = new Document();
        old.ApplyTo(document);
        Assert.Equal(ArrowHeadKind.Open, document.Layers[0].Shapes[0].LineEndHead);

        var current = new DocumentFile { Width = 100, Height = 100 };
        current.Layers.Add(new LayerFile { Name = "L", Shapes = [Loop(ShapeKind.LoopArrowClockwise, ArrowHeadKind.None, ArrowHeadKind.None)] });
        var again = new Document();
        current.ApplyTo(again);
        Assert.Equal(ArrowHeadKind.None, again.Layers[0].Shapes[0].LineEndHead);
    }
}
