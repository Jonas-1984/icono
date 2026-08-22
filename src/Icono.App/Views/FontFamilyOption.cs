namespace Icono.App.Views;

/// <summary>One entry in the Text Font dropdown — a real class (not a bare string) so WPF's
/// binding engine can resolve <see cref="Label"/> via <c>DisplayMemberPath</c>.</summary>
public sealed record FontFamilyOption(string Label);
