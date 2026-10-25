using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Icono.App.State;
using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.App.Views;

/// <summary>Click the number of a wall dimension and type the length you want: the wall is made that long (like the room planners
/// of furniture shops).</summary>
public partial class CanvasHostView
{
    private TextBox? _lengthBox;
    private Shape? _lengthWall;
    private int _lengthSegment;
    private Shape? _lengthSymbol;
    private SymbolDimensionPart _lengthPart;

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

    /// <summary>Opens the editor when the point is on the number of a dimension of a door, window or opening: the width resizes the
    /// opening, a distance to a wall corner slides it along the wall.</summary>
    private bool TryBeginSymbolDimensionEdit(Point point)
    {
        if (_context is null)
        {
            return false;
        }

        foreach (var layer in _context.Document.Layers.Where(l => l.Visible && !l.Locked))
        {
            foreach (var symbol in layer.Shapes.Where(s => ArchitecturalSymbols.IsSymbol(s.Kind) && !s.Locked))
            {
                if (SymbolMeasurements.HitTestText(_context.Document, symbol, point.X, point.Y, 3 / Zoom) is { } dimension)
                {
                    BeginSymbolDimensionEdit(symbol, dimension);
                    return true;
                }
            }
        }

        return false;
    }

    private void BeginSymbolDimensionEdit(Shape symbol, SymbolDimension dimension)
    {
        if (_context is null)
        {
            return;
        }

        CancelTextEdit();
        RemoveLengthBox();
        _lengthSymbol = symbol;
        _lengthPart = dimension.Part;
        _context.SelectedShape = symbol;

        OpenLengthBox(
            DimensionRenderer.Format(dimension.Length, symbol.DimUnit, symbol.DimPrecision),
            dimension.Layout.TextPosition,
            dimension.Part == SymbolDimensionPart.Width
                ? "Type the width of the opening (for example 90, 1.2 m or 900 mm) and press Enter. Esc cancels."
                : "Type the clear distance to the wall corner (for example 25 or 0.4 m) and press Enter: the symbol slides along the wall. Esc cancels.");
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

        OpenLengthBox(
            DimensionRenderer.Format(length, wall.WallDimensionUnit, wall.DimPrecision),
            layout.TextPosition,
            "Type the length (for example 250, 2.5 m or 2500 mm) and press Enter. Esc cancels.");
    }

    private void OpenLengthBox(string text, SKPoint position, string toolTip)
    {
        if (_context is null)
        {
            return;
        }

        var textBox = new TextBox
        {
            Text = text,
            Width = 110,
            Height = 28,
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderBrush = (Brush)FindResource("AccentBlueMidBrush"),
            BorderThickness = new Thickness(1.5),
            ToolTip = toolTip,
        };

        var centre = SkiaCanvas.TranslatePoint(new Point(position.X * Zoom, position.Y * Zoom), TextEditOverlay);
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
        if (_lengthBox is not null && _lengthSymbol is { } symbol && _context is not null)
        {
            CommitSymbolDimension(symbol);
            return;
        }

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

    private void CommitSymbolDimension(Shape symbol)
    {
        var part = _lengthPart;
        var text = _lengthBox!.Text.Trim();
        RemoveLengthBox();
        if (_context is null)
        {
            return;
        }

        // 0 is a fine distance (the symbol against the corner); a width has to be positive
        double px;
        var zero = part != SymbolDimensionPart.Width && text is "0" or "0.0" or "0,0";
        if (zero)
        {
            px = 0;
        }
        else if (!WallGeometry.TryParseLength(text, symbol.DimUnit, _context.Document.ScaleDenominator, out px))
        {
            _context.Notify("That is not a length. Type for example 90, 1.2 m or 900 mm.");
            return;
        }

        var changed = part == SymbolDimensionPart.Width
            ? _context.SetSymbolWidth(symbol, px)
            : _context.SetSymbolGap(symbol, part, px);
        if (changed)
        {
            _context.Notify(part == SymbolDimensionPart.Width ? "Opening width set." : "Distance to the wall corner set.");
        }
        else if (part != SymbolDimensionPart.Width)
        {
            _context.Notify("The distance could not be set: the symbol has to lie in a wall.");
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
        _lengthSymbol = null;
        box.KeyDown -= LengthBox_OnKeyDown;
        box.LostFocus -= LengthBox_OnLostFocus;
        TextEditOverlay.Children.Remove(box);
    }
}
