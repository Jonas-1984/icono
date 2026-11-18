using Icono.Core;
using Icono.Editor;

namespace Icono.Tests;

/// <summary>The Region Color tool: the shape under the place takes the current colour (one undo step); a design grid is found square by square.</summary>
public class RegionColoringTests
{
    private static (EditorContext Context, Shape Box) Setup()
    {
        var context = new EditorContext();
        context.Document.Layers.Add(new Layer { Name = "L" });
        var box = new Shape { Kind = ShapeKind.Rectangle, X = 100, Y = 100, Width = 80, Height = 60, FillColor = ArgbColor.FromRgb(1, 2, 3), FillEnabled = false };
        context.Document.Layers[0].Shapes.Add(box);
        context.CurrentFillColor = ArgbColor.FromRgb(200, 10, 20);
        return (context, box);
    }

    [Fact]
    public void ATap_OnAShape_ColoursIt_AndFillsIt()
    {
        var (context, box) = Setup();

        var done = RegionColoring.Apply(context, 140, 130);

        Assert.True(done);
        Assert.Equal(ArgbColor.FromRgb(200, 10, 20), box.FillColor);
        Assert.True(box.FillEnabled);
        context.Undo();
        Assert.Equal(ArgbColor.FromRgb(1, 2, 3), box.FillColor);
    }

    [Fact]
    public void ATap_BesideEverything_ColoursNothing()
    {
        var (context, box) = Setup();

        Assert.False(RegionColoring.Apply(context, 400, 400));
        Assert.Equal(ArgbColor.FromRgb(1, 2, 3), box.FillColor);
    }

    [Fact]
    public void ALockedShape_IsLeftAlone()
    {
        var (context, box) = Setup();
        box.Locked = true;

        Assert.False(RegionColoring.Apply(context, 140, 130));
        Assert.Equal(ArgbColor.FromRgb(1, 2, 3), box.FillColor);
    }

    [Fact]
    public void ADesignGrid_GivesItsSquare()
    {
        var context = new EditorContext();
        context.Document.Layers.Add(new Layer { Name = "L" });
        var grid = new Shape { Kind = ShapeKind.DesignGrid, X = 50, Y = 50, Width = 100, Height = 100, GridCellSize = 10 };
        context.Document.Layers[0].Shapes.Add(grid);

        var found = RegionColoring.FindGridCell(context, 73, 91);

        Assert.NotNull(found);
        Assert.Same(grid, found!.Value.Grid);
        Assert.Equal(4, found.Value.Row);   // (91 - 50) / 10
        Assert.Equal(2, found.Value.Col);   // (73 - 50) / 10
        Assert.Null(RegionColoring.FindGridCell(context, 400, 400));
    }

    [Fact]
    public void ALockedGrid_IsNotFound()
    {
        var context = new EditorContext();
        context.Document.Layers.Add(new Layer { Name = "L" });
        context.Document.Layers[0].Shapes.Add(new Shape { Kind = ShapeKind.DesignGrid, X = 0, Y = 0, Width = 100, Height = 100, GridCellSize = 10, Locked = true });

        Assert.Null(RegionColoring.FindGridCell(context, 50, 50));
    }
}
