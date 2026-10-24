namespace Icono.Core;

/// <summary>Which edge of the sheet frame the title block (project, address, scale, date, position) sits on.</summary>
public enum SheetPlacement
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>
/// The sheet of a page: a frame round the drawing area and a title block with the project data. Immutable; a change is a new value
/// (<c>sheet with { Project = "..." }</c>), which is what lets undo keep the old one. Lengths are in millimetres on the paper.
/// </summary>
public sealed record SheetInfo
{
    public const double PxPerMm = 96.0 / 25.4;

    public bool Enabled { get; init; }

    public string Project { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    /// <summary>The position (sheet or plan number) shown in the title block.</summary>
    public string Position { get; init; } = string.Empty;

    public string Date { get; init; } = string.Empty;

    public SheetPlacement Placement { get; init; } = SheetPlacement.Bottom;

    /// <summary>Distance between the page edge and the frame.</summary>
    public double MarginMm { get; init; } = 8;

    /// <summary>Thickness of the title block (its height at the top / bottom, its width at the left / right). 0 = automatic.</summary>
    public double BlockSizeMm { get; init; }

    public static double AutomaticBlockSizeMm(SheetPlacement placement) =>
        placement is SheetPlacement.Top or SheetPlacement.Bottom ? 18 : 55;

    public double EffectiveBlockSizeMm => BlockSizeMm > 0 ? BlockSizeMm : AutomaticBlockSizeMm(Placement);

    public bool IsHorizontal => Placement is SheetPlacement.Top or SheetPlacement.Bottom;
}
