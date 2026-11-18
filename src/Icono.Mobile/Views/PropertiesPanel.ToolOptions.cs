using Icono.Core;
using Icono.Editor;
using Icono.Rendering;

namespace Icono.Mobile.Views;

/// <summary>What the panel shows while one of the other tools is the tool (as it shows the settings of the Pen): the kind of dimension, what Match
/// carries over, the colour of the bucket and of the colour picker - and the card of a design grid.</summary>
public sealed partial class PropertiesPanel
{
    private static readonly (string Name, DimensionKind Value)[] DimensionKinds =
    [
        ("Aligned (along the line)", DimensionKind.Aligned), ("Horizontal", DimensionKind.Horizontal), ("Vertical", DimensionKind.Vertical),
        ("Angle", DimensionKind.Angular), ("Radius", DimensionKind.Radius), ("Diameter", DimensionKind.Diameter),
    ];

    /// <summary>True while the panel shows the settings of a tool instead of the selection (it is built for the tool).</summary>
    private bool BuildToolOptions(EditorContext context)
    {
        switch (context.ActiveTool)
        {
            case ActiveTool.PathDraw when context.SelectedShapeKind == ShapeKind.Dimension:
                BuildDimensionTool(context);
                return true;
            case ActiveTool.Match:
                BuildMatchTool(context);
                return true;
            case ActiveTool.RegionColor:
                BuildBucketTool(context);
                return true;
            case ActiveTool.Eyedropper:
                Title("Colour picker");
                Note("Tap a spot of the drawing: its colour becomes the colour of the next shapes. The colour now:");
                ColorRow(context.CurrentFillColor, c => context.CurrentFillColor = c);
                return true;
            default:
                return false;
        }
    }

    private void BuildDimensionTool(EditorContext context)
    {
        Title("Dimension");
        Heading("KIND");
        Choice("Kind", DimensionKinds, context.CurrentDimensionKind, kind => context.CurrentDimensionKind = kind);
        Note(context.CurrentDimensionKind switch
        {
            DimensionKind.Angular => "Angle: tap the corner, a point on each of the two sides, then where the arc runs.",
            DimensionKind.Radius or DimensionKind.Diameter => "Tap the centre and a point on the circle - the dimension is attached to the circle.",
            _ => "Tap the two points to measure, then where the dimension line runs. A point on a shape sticks to it.",
        });
        Note("Changing the kind starts the next dimension; Done ends the one you are making.");
    }

    private void BuildMatchTool(EditorContext context)
    {
        Title("Match");
        Note(context.MatchSource is null
            ? "Tap the object whose look you want to copy."
            : "Source picked. Tap the objects that should get its look; tap the source again to pick another, tap beside everything to end.");

        Heading("WHAT IS CARRIED OVER");
        void Aspect(string label, MatchAspects aspect) =>
            Toggle(label, context.MatchAspects.HasFlag(aspect), on =>
            {
                context.MatchAspects = on ? context.MatchAspects | aspect : context.MatchAspects & ~aspect;
                OnChanged();
            });

        Aspect("Fill", MatchAspects.Fill);
        Aspect("Border / line", MatchAspects.Border);
        Aspect("Opacity, blur, shadow", MatchAspects.Effects);
        Aspect("Corners and design style", MatchAspects.Form);
        Aspect("Text", MatchAspects.Text);
        Aspect("Line ends and dashes", MatchAspects.Line);
        Aspect("Wall", MatchAspects.Wall);
        Aspect("Dimension look", MatchAspects.Dimension);
        Aspect("Size", MatchAspects.Size);
        ButtonRow(("Dimension look to all dimensions", context.MatchDimensionStyleToAll));
    }

    private void BuildBucketTool(EditorContext context)
    {
        Title("Region colour");
        Note("Tap a shape to colour it with this colour. On a design grid, slide your finger over the squares to paint them.");
        ColorRow(context.CurrentFillColor, c => context.CurrentFillColor = c);
        Toggle("Erase squares of a design grid", context.GridPaintErase, on => context.GridPaintErase = on);
    }

    // ---------------------------------------------------------------- design grid

    /// <summary>The card of a design grid: size of a square, number of columns and rows, the hairlines, and clearing the painted squares.</summary>
    private void BuildDesignGrid(EditorContext context, IReadOnlyList<Shape> shapes)
    {
        var grids = shapes.Where(s => s.Kind == ShapeKind.DesignGrid).ToList();
        if (grids.Count == 0)
        {
            return;
        }

        var grid = grids[0];
        bool IsGrid(Shape s) => s.Kind == ShapeKind.DesignGrid;
        Heading("DESIGN GRID");
        NumberRow("Square", grid.GridCellSize / DesignGridMath.PxPerMm, "mm", 0.5, 0.5, 50, "0.##", mm => context.SetOnSelection(s =>
        {
            // the number of squares stays, the grid is scaled round them
            var columns = DesignGridMath.Columns(s);
            var rows = DesignGridMath.Rows(s);
            s.GridCellSize = mm * DesignGridMath.PxPerMm;
            s.Width = columns * s.GridCellSize;
            s.Height = rows * s.GridCellSize;
        }, IsGrid));
        NumberRow("Columns", DesignGridMath.Columns(grid), "", 1, 1, 2000, "0", n => context.SetOnSelection(s => s.Width = (int)n * s.GridCellSize, IsGrid));
        NumberRow("Rows", DesignGridMath.Rows(grid), "", 1, 1, 2000, "0", n => context.SetOnSelection(s => s.Height = (int)n * s.GridCellSize, IsGrid));
        Toggle("Show the lines", grid.GridShowLines, on => context.SetOnSelection(s => s.GridShowLines = on, IsGrid));
        ButtonRow(("Clear all squares", () => context.SetOnSelection(s => s.GridCells = null, IsGrid)));
        Note("Paint the squares with the Region colour tool (More): choose the colour, then slide over the squares.");
    }
}
