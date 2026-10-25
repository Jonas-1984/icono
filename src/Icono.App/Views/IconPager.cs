using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Icono.App.Views;

/// <summary>
/// Pages a WrapPanel's children (a tool palette's icon buttons) into fixed-size pages, showing
/// only the current page and rendering a row of clickable dot indicators. The WrapPanel's own
/// MinHeight (set in XAML, sized for the tallest page) keeps the card from growing/shrinking as
/// the user switches pages — dots are the only navigation, no prev/next arrows.
/// </summary>
public sealed class IconPager
{
    private static readonly Brush ActiveDotBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0xB0, 0xFF));
    private static readonly Brush InactiveDotBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xA0, 0xA0, 0xA0));

    private readonly WrapPanel _panel;
    private readonly StackPanel _dots;
    private readonly int _pageSize;
    private int _page;

    public IconPager(WrapPanel panel, StackPanel dots, int pageSize)
    {
        _panel = panel;
        _dots = dots;
        _pageSize = pageSize;
        Apply();
    }

    private void GoTo(int page)
    {
        _page = page;
        Apply();
    }

    private void Apply()
    {
        var start = _page * _pageSize;
        var end = start + _pageSize;

        for (var i = 0; i < _panel.Children.Count; i++)
        {
            _panel.Children[i].Visibility = i >= start && i < end ? Visibility.Visible : Visibility.Collapsed;
        }

        var totalPages = Math.Max(1, (int)Math.Ceiling(_panel.Children.Count / (double)_pageSize));
        RebuildDots(totalPages);
    }

    private void RebuildDots(int totalPages)
    {
        _dots.Children.Clear();

        for (var i = 0; i < totalPages; i++)
        {
            var pageIndex = i;
            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(4, 0, 4, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = i == _page ? ActiveDotBrush : InactiveDotBrush,
                Effect = i == _page ? new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0x5A, 0xB0, 0xFF), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 } : null,
            };
            dot.MouseLeftButtonDown += (_, _) => GoTo(pageIndex);
            _dots.Children.Add(dot);
        }
    }
}
