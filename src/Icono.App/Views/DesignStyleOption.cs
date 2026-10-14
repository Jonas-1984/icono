using System.Windows.Media;
using Icono.Core;

namespace Icono.App.Views;

/// <summary>One entry of the "DESIGN STYLE" picker: which style it applies, its label/tooltip, a live
/// preview thumbnail of that style in the selected shape's colour, and whether it is the active one.</summary>
public sealed class DesignStyleOption : ObservableObject
{
    private ImageSource? _thumbnail;
    private bool _isSelected;

    public required DesignStyleKind Kind { get; init; }

    public required string Label { get; init; }

    public required string ToolTip { get; init; }

    public ImageSource? Thumbnail { get => _thumbnail; set => SetField(ref _thumbnail, value); }

    public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
}
