using Icono.Core;

namespace Icono.App.Views;

/// <summary>One entry in the Start/End "line ends" dropdowns — a real class (not a ValueTuple) so
/// WPF's binding engine can resolve <see cref="Label"/> via <c>DisplayMemberPath</c>.</summary>
public sealed record ArrowHeadOption(string Label, ArrowHeadKind Value)
{
    /// <summary>The short name for the small quick box next to a line ("Open" instead of "Open Arrow").</summary>
    public string Short => Label.Replace(" Arrow", string.Empty, StringComparison.Ordinal);
}
