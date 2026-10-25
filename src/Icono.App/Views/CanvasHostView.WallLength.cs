using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Icono.App.State;
using Icono.Core;
using Icono.Rendering;

namespace Icono.App.Views;

/// <summary>Click the number of a wall dimension and type the length you want: the wall is made that long (like the room planners
/// of furniture shops).</summary>
public partial class CanvasHostView
{
    private TextBox? _lengthBox;
    private Shape? _lengthWall;
    private int _lengthSegment;

    /// <summary>Opens the length editor when the point is on the number of a wall dimension. True when it did.</summary>
    private bool TryBeginWallLengthEdit(Point point)
    {
        if (_context is null)
        {
            return false;
        }

        foreach (var layer in _context.Document.Layers.Where(l => l.Visible && !l.Locked))
        {
            foreach (var wall in layer.Shapes.Where(s => s.Kind == ShapeKind.Wall && !s.Locked))
            {
                if (WallRenderer.HitTestDimensionText(wall, point.X, point.Y, 3 / Zoom) is { } segment)
                {
                    BeginWallLengthEdit(wall, segment);
                    return true;
                }
            }
        }

        return false;
    }

    private void BeginWallLengthEdit(Shape wall, int segment)
    {
        if (_context is null || WallRenderer.LayoutSegmentDimension(wall, segment) is not { } layout)
        {
            return;
        }

        CancelTextEdit();
        RemoveLengthBox();

        var length = WallGeometry.MeasuredLength(wall, segment);
        _lengthWall = wall;
        _lengthSegment = segment;
        _context.SelectedShape = wall;

        var textBox = new TextBox
        {
            Text = DimensionRenderer.Format(length, wall.WallDimensionUnit, wall.DimPrecision),
            Width = 110,
            Height = 28,
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderBrush = (Brush)FindResource("AccentBlueMidBrush"),
            BorderThickness = new Thickness(1.5),
            ToolTip = "Type the length (for example 250, 2.5 m or 2500 mm) and press Enter. Esc cancels.",
        };

        var centre = SkiaCanvas.TranslatePoint(new Point(layout.TextPosition.X * Zoom, layout.TextPosition.Y * Zoom), TextEditOverlay);
        Canvas.SetLeft(textBox, centre.X - textBox.Width / 2);
        Canvas.SetTop(textBox, centre.Y - textBox.Height / 2);

        TextEditOverlay.Children.Add(textBox);
        _lengthBox = textBox;
        textBox.KeyDown += LengthBox_OnKeyDown;
        textBox.LostFocus += LengthBox_OnLostFocus;

        textBox.Focus();
        Keyboard.Focus(textBox);
        textBox.SelectAll();
        _context.RequestRedraw();
    }

    private void LengthBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitWallLength();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            RemoveLengthBox();
            e.Handled = true;
        }
    }

    private void LengthBox_OnLostFocus(object sender, RoutedEventArgs e) => CommitWallLength();

    /// <summary>Applies the typed length (nothing happens when it cannot be read or does not change the wall).</summary>
    private void CommitWallLength()
    {
        if (_lengthBox is null || _lengthWall is not { } wall || _context is null)
        {
            return;
        }

        var text = _lengthBox.Text;
        var segment = _lengthSegment;
        RemoveLengthBox();

        if (!WallGeometry.TryParseLength(text, wall.WallDimensionUnit, _context.Document.ScaleDenominator, out var px))
        {
            _context.Notify("That is not a length. Type for example 250, 2.5 m or 2500 mm.");
            return;
        }

        if (_context.SetWallSegmentLength(wall, segment, px))
        {
            _context.Notify($"Wall set to {DimensionRenderer.Format(px, wall.WallDimensionUnit, wall.DimPrecision, _context.Document.ScaleDenominator)}.");
        }
    }

    /// <summary>A click outside the length editor keeps what was typed; a click inside it changes nothing.</summary>
    private void CommitWallLengthUnlessInside(object? source)
    {
        if (_lengthBox is null)
        {
            return;
        }

        for (var d = source as DependencyObject; d is not null;)
        {
            if (ReferenceEquals(d, _lengthBox))
            {
                return;
            }

            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }

        CommitWallLength();
    }

    private void RemoveLengthBox()
    {
        if (_lengthBox is not { } box)
        {
            return;
        }

        _lengthBox = null;
        _lengthWall = null;
        box.KeyDown -= LengthBox_OnKeyDown;
        box.LostFocus -= LengthBox_OnLostFocus;
        TextEditOverlay.Children.Remove(box);
    }
}
