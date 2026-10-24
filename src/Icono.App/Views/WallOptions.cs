using Icono.Core;

namespace Icono.App.Views;

/// <summary>One entry of the wall "Hatch" drop-down (a class, not a tuple, so DisplayMemberPath can read it).</summary>
public sealed record HatchOption(string Label, HatchKind Value);

/// <summary>One entry of the wall dimension-unit drop-down.</summary>
public sealed record LengthUnitOption(string Label, LengthUnit Value);

/// <summary>One entry of the drawing-scale drop-down (1:N).</summary>
public sealed record ScaleOption(string Label, double Value);

/// <summary>One entry of the "which dimension" drop-down of a wall (Index -1 = all of them, the wall's own settings).</summary>
public sealed record SegmentOption(string Label, int Index);

/// <summary>One entry of the dimension end-mark drop-down.</summary>
public sealed record ArrowStyleOption(string Label, DimensionArrowStyle Value);
