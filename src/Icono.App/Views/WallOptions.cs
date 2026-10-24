using Icono.Core;

namespace Icono.App.Views;

/// <summary>One entry of the wall "Hatch" drop-down (a class, not a tuple, so DisplayMemberPath can read it).</summary>
public sealed record HatchOption(string Label, HatchKind Value);

/// <summary>One entry of the wall dimension-unit drop-down.</summary>
public sealed record LengthUnitOption(string Label, LengthUnit Value);
