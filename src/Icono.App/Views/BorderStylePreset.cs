using System.Windows.Media;
using Icono.Core;

namespace Icono.App.Views;

/// <summary>A style-presets grid entry. Most presets just recolor the border (the default
/// behavior in <c>PropertiesPanelView.BorderPreset_OnMouseLeftButtonDown</c>); presets that need
/// to touch more than the border color (e.g. "Acrylic") supply <see cref="CustomApply"/> instead.</summary>
public sealed record BorderStylePreset(string Name, Brush Preview, Action<Shape>? CustomApply = null);
