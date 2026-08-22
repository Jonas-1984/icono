using Icono.Core;

namespace Icono.App.Views;

/// <summary>One entry in the Fade Direction dropdown — a real class (not a bare enum value) so
/// WPF's binding engine can resolve <see cref="Label"/> via <c>DisplayMemberPath</c>.</summary>
public sealed record FadeDirectionOption(string Label, FadeDirection Value);
