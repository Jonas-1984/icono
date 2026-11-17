using Icono.Core;
using Icono.Rendering;

namespace Icono.Editor;

/// <summary>Typing the length of a wall, or the width of a door / window: the text shown for the number that was tapped or clicked, and what
/// happens with the text that was typed - shared by the Windows program and the tablet app. The wall is made that long (the length is the one
/// its dimension shows, the inside of a room by default); a door or window gets that width, or slides along its wall to that distance from
/// the corner. Each is one undo step (<see cref="EditorContext.SetWallSegmentLength"/>, <see cref="EditorContext.SetSymbolWidth"/> ...).</summary>
public static class LengthEditing
{
    /// <summary>What to tell the user to type for a wall length.</summary>
    public const string WallNote = "Type the length (for example 250, 2.5 m or 2500 mm).";

    /// <summary>The length of a wall segment as its dimension writes it (what the editor starts with).</summary>
    public static string WallText(Shape wall, int segment) =>
        DimensionRenderer.Format(WallGeometry.MeasuredLength(wall, segment), WallRenderer.SegmentUnit(wall, segment), WallRenderer.SegmentPrecision(wall, segment));

    /// <summary>The dimension of a door / window as it is written (what the editor starts with).</summary>
    public static string SymbolText(Shape symbol, SymbolDimension dimension) =>
        DimensionRenderer.Format(dimension.Length, symbol.DimUnit, symbol.DimPrecision);

    /// <summary>What to tell the user to type for this dimension of a door / window.</summary>
    public static string SymbolNote(SymbolDimensionPart part) => part == SymbolDimensionPart.Width
        ? "Type the width of the opening (for example 90, 1.2 m or 900 mm)."
        : "Type the clear distance to the wall corner (for example 25 or 0.4 m): the symbol slides along the wall.";

    /// <summary>A short name for the dimension, for the title of the editor.</summary>
    public static string SymbolTitle(SymbolDimensionPart part) => part == SymbolDimensionPart.Width ? "Opening width" : "Distance to the wall corner";

    /// <summary>Applies the typed length to a wall segment; tells the user what happened (<see cref="EditorContext.Notify"/>). True when the wall changed.</summary>
    public static bool CommitWall(EditorContext context, Shape wall, int segment, string? text)
    {
        var unit = WallRenderer.SegmentUnit(wall, segment);
        if (!WallGeometry.TryParseLength(text, unit, context.Document.ScaleDenominator, out var px))
        {
            context.Notify("That is not a length. Type for example 250, 2.5 m or 2500 mm.");
            return false;
        }

        if (!context.SetWallSegmentLength(wall, segment, px))
        {
            return false;
        }

        context.Notify($"Wall set to {DimensionRenderer.Format(px, unit, WallRenderer.SegmentPrecision(wall, segment), context.Document.ScaleDenominator)}.");
        return true;
    }

    /// <summary>Applies the typed width, or the typed distance to the wall corner, of a door / window; tells the user what happened. True when it changed.</summary>
    public static bool CommitSymbol(EditorContext context, Shape symbol, SymbolDimensionPart part, string? text)
    {
        text = text?.Trim() ?? "";

        // 0 is a fine distance (the symbol against the corner); a width has to be positive
        double px;
        if (part != SymbolDimensionPart.Width && text is "0" or "0.0" or "0,0")
        {
            px = 0;
        }
        else if (!WallGeometry.TryParseLength(text, symbol.DimUnit, context.Document.ScaleDenominator, out px))
        {
            context.Notify("That is not a length. Type for example 90, 1.2 m or 900 mm.");
            return false;
        }

        var changed = part == SymbolDimensionPart.Width
            ? context.SetSymbolWidth(symbol, px)
            : context.SetSymbolGap(symbol, part, px);
        if (changed)
        {
            context.Notify(part == SymbolDimensionPart.Width ? "Opening width set." : "Distance to the wall corner set.");
        }
        else if (part != SymbolDimensionPart.Width)
        {
            context.Notify("The distance could not be set: the symbol has to lie in a wall.");
        }

        return changed;
    }
}
