using Icono.Core;
using Icono.Rendering;

namespace Icono.Editor;

/// <summary>How the drawing tools make new objects - the defaults every new rectangle, line, wall, dimension, door ... starts with, and the
/// real-world size a door or symbol gets from a tap. Shared by the Windows program and the tablet app, so both make the same objects.</summary>
public static class ShapeTemplates
{
    /// <summary>The line tools that are dragged from one point to another (all other line tools place point after point).</summary>
    public static readonly IReadOnlySet<ShapeKind> TwoPointLineKinds = new HashSet<ShapeKind> { ShapeKind.Line, ShapeKind.Arrow, ShapeKind.DoubleArrow };

    /// <summary>How many points a dimension of this kind needs: its points, the last one fixing where the dimension line runs.</summary>
    public static int DimensionPointsNeeded(DimensionKind kind) => kind switch
    {
        DimensionKind.Angular => 4,
        DimensionKind.Radius or DimensionKind.Diameter or DimensionKind.Length => 2,
        _ => 3,
    };

    /// <summary>A line or arrow that starts at (<paramref name="x"/>, <paramref name="y"/>) and is dragged to its end.</summary>
    public static Shape NewTwoPointLine(ShapeKind kind, double x, double y, ArgbColor fill) => new()
    {
        Kind = kind,
        X = x,
        Y = y,
        Width = 1,
        Height = 1,
        FillColor = fill,
        Points = [new CorePoint(0, 0), new CorePoint(0, 0)],
        // The Arrow/Double Arrow tools' look - a plain open V head - is the default of their line ends, freely changed afterwards.
        LineEndHead = kind is ShapeKind.Arrow or ShapeKind.DoubleArrow ? ArrowHeadKind.Open : ArrowHeadKind.None,
        LineStartHead = kind == ShapeKind.DoubleArrow ? ArrowHeadKind.Open : ArrowHeadKind.None,
    };

    /// <summary>A shape with a box (rectangle, ellipse, door, window, symbol, catalogue unit ...) that starts at (<paramref name="x"/>,
    /// <paramref name="y"/>) and is dragged open; <paramref name="unit"/> is the catalogue unit when the kind is CombinedPath.</summary>
    public static Shape NewBox(ShapeKind kind, double x, double y, ArgbColor fill, UnitObject? unit = null)
    {
        var shape = new Shape
        {
            Kind = kind,
            X = x,
            Y = y,
            Width = 0,
            Height = 0,
            FillColor = fill,
            CornerRadius = kind == ShapeKind.RoundedRectangle ? 12 : 0,
        };

        if (ArchitecturalSymbols.IsSymbol(kind))
        {
            ArchitecturalSymbols.ApplyDefaults(shape);
        }

        if (PlanSymbols.IsPlanSymbol(kind))
        {
            PlanSymbols.ApplyDefaults(shape);
        }

        if (ShapeGeometryFactory.IsLoopArrow(kind))
        {
            // the circular arrows start with an open head at the end, like an Arrow; the line ends can be changed afterwards
            shape.LineEndHead = ArrowHeadKind.Open;
        }

        if (kind == ShapeKind.CombinedPath && unit is not null)
        {
            // a cabinet or unit of the catalogue: white body, thin dark line, as drawn in the catalogue
            shape.CustomPathData = unit.PathData;
            shape.CustomPathStrokeOnly = true;
            shape.FillColor = ArgbColor.FromRgb(0xFF, 0xFF, 0xFF);
            shape.BorderColor = ArgbColor.FromRgb(0x23, 0x29, 0x33);
            shape.BorderThickness = 1;
        }

        if (kind == ShapeKind.DesignGrid)
        {
            // Fine, light hairlines so the coloured squares stay the star of the grid.
            shape.BorderColor = ArgbColor.FromRgb(0xB4, 0xBA, 0xC4);
            shape.BorderThickness = 0.5;
        }

        return shape;
    }

    /// <summary>A wall, polyline, curve or dimension whose first point is (<paramref name="x"/>, <paramref name="y"/>): it holds that point and a
    /// second one that follows the pointer until it is placed.</summary>
    public static Shape NewPath(ShapeKind kind, double x, double y, ArgbColor fill, DimensionKind dimensionKind, LengthUnit dimensionUnit, double scale)
    {
        var shape = new Shape
        {
            Kind = kind,
            X = x,
            Y = y,
            Width = 1,
            Height = 1,
            FillColor = fill,
            Points = [new CorePoint(0, 0), new CorePoint(0, 0)],
        };

        if (kind == ShapeKind.Dimension)
        {
            DimensionBuilder.ApplyStyle(shape);
            shape.DimKind = dimensionKind;
            shape.DimUnit = dimensionUnit;
        }

        if (kind == ShapeKind.Wall)
        {
            WallEditor.ApplyDrawingDefaults(shape, scale);
        }

        return shape;
    }

    /// <summary>Finishes a box shape that was only tapped or barely dragged: a design grid is made of whole squares (a tap drops a 24 x 24
    /// grid); a door, window, plan symbol or catalogue unit gets its real-world size at the drawing scale.</summary>
    public static void ApplyClickSize(Shape shape, Document document, UnitObject? unit = null)
    {
        if (shape.Kind == ShapeKind.DesignGrid)
        {
            var cell = shape.GridCellSize;
            var clickOnly = shape.Width < cell * 2 && shape.Height < cell * 2;
            shape.Width = (clickOnly ? 24 : Math.Max(1, Math.Round(shape.Width / cell))) * cell;
            shape.Height = (clickOnly ? 24 : Math.Max(1, Math.Round(shape.Height / cell))) * cell;
        }

        (double Width, double Height)? size = null;
        if (ArchitecturalSymbols.IsSymbol(shape.Kind) && shape.Width < 6 && (shape.Height < 6 || ArchitecturalSymbols.IsDoor(shape.Kind)))
        {
            // a click, not a drag (a door's height follows from its width, so only the width tells)
            size = ArchitecturalSymbols.DefaultSize(shape.Kind, shape.WallThickness, document.ScaleDenominator, document.Width);
        }
        else if (PlanSymbols.IsPlanSymbol(shape.Kind) && shape.Width < 6 && shape.Height < 6)
        {
            size = PlanSymbols.DefaultSize(shape.Kind, document.ScaleDenominator, document.Width);
        }
        else if (shape.Kind == ShapeKind.CombinedPath && unit is not null && shape.CustomPathData == unit.PathData && shape.Width < 6 && shape.Height < 6)
        {
            size = UnitCatalog.DefaultSize(unit, document.ScaleDenominator);
        }

        if (size is { } wanted)
        {
            var outline = ShapeOutline.FrameForBox(shape, shape.X, shape.Y, wanted.Width, wanted.Height);
            (shape.X, shape.Y, shape.Width, shape.Height) = (outline.X, outline.Y, outline.W, outline.H);
        }
    }

    /// <summary>True when a new shape is big enough to be kept: a box at least 2 x 2, a two-point line at least 2 long in one direction.</summary>
    public static bool IsBigEnough(Shape shape) =>
        shape.Points is { Count: >= 2 } ? shape.Width >= 2 || shape.Height >= 2 : shape is { Width: >= 2, Height: >= 2 };
}
