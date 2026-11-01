using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Icono.App.State;
using Icono.App.State.Undo;
using Icono.Core;
using Icono.Rendering;

namespace Icono.App.Views;

/// <summary>Quick editor that floats next to a selected line/arrow: its form and the shape of both ends.
/// Every change is one undo step and goes through the shape, so the Properties panel follows along.</summary>
public partial class LineQuickPopup : UserControl
{
    private EditorContext? _context;
    private Shape? _shape;
    private bool _syncing;

    public LineQuickPopup()
    {
        InitializeComponent();
        StartHeadComboBox.ItemsSource = PropertiesPanelView.ArrowHeadOptions;
        EndHeadComboBox.ItemsSource = PropertiesPanelView.ArrowHeadOptions;
        BuildSwatches();
    }

    /// <summary>Shows the current values of <paramref name="shape"/> in the controls.</summary>
    public void Bind(EditorContext context, Shape shape)
    {
        _context = context;
        _shape = shape;

        // a loop arrow has no form to choose (it is an arc): only its two ends
        var isLoop = ShapeGeometryFactory.IsLoopArrow(shape.Kind);
        FormRow.Visibility = isLoop ? Visibility.Collapsed : Visibility.Visible;
        var isLine = ShapeGeometryFactory.IsLineFamily(shape.Kind);
        RadiusBox.Visibility = RadiusLabel.Visibility = isLine ? Visibility.Visible : Visibility.Collapsed;
        NumbersGrid.Margin = new Thickness(0, isLoop ? 0 : 12, 0, 0);
        LineEndTitle.Margin = new Thickness(2, isLoop ? 0 : 12, 0, 6);

        _syncing = true;
        try
        {
            RefreshNumbers(shape);
            DashedCheckBox.IsChecked = shape.Dashed;
            StartHeadComboBox.SelectedItem = PropertiesPanelView.ArrowHeadOptions.First(o => o.Value == shape.LineStartHead);
            EndHeadComboBox.SelectedItem = PropertiesPanelView.ArrowHeadOptions.First(o => o.Value == shape.LineEndHead);

            foreach (var toggle in new[] { StraightToggle, ElbowToggle, RoundedToggle, CurvedToggle })
            {
                toggle.IsChecked = (string)toggle.Tag == shape.PathRoutingStyle.ToString();
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    // The canvas underneath selects / deselects on mouse clicks — clicks on this card must stay here.
    private void Card_OnMouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void StyleToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag } || _shape is null)
        {
            return;
        }

        var style = Enum.Parse<PathRoutingStyle>(tag);
        Edit(s =>
        {
            s.PathRoutingStyle = style;

            // A rounded elbow needs a visible radius (same rule as the CONNECTOR STYLE card).
            if (style == PathRoutingStyle.Rounded && s.CornerRadius <= 0)
            {
                s.CornerRadius = 8;
            }
        });

        // Re-sync so exactly the chosen style is lit (clicking the active one must not switch it off).
        Bind(_context!, _shape);
    }

    private void StartHeadComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StartHeadComboBox.SelectedItem is ArrowHeadOption option)
        {
            Edit(s => s.LineStartHead = option.Value);
        }
    }

    private void EndHeadComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EndHeadComboBox.SelectedItem is ArrowHeadOption option)
        {
            Edit(s => s.LineEndHead = option.Value);
        }
    }

    private const double PxPerCm = 96.0 / 2.54;

    private static readonly uint[] SwatchColors =
        [0x1B2332, 0x6B7280, 0xE54B4B, 0xF39C12, 0xF4D03F, 0x2ECC71, 0x3498DB, 0x9B59B6, 0xFFFFFF];

    private void BuildSwatches()
    {
        foreach (var rgb in SwatchColors)
        {
            var color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            var swatch = new Button
            {
                Width = 18, Height = 18, Margin = new Thickness(2, 0, 2, 4), Padding = new Thickness(0), Tag = color,
                Background = new SolidColorBrush(color), BorderThickness = new Thickness(1), ToolTip = $"#{rgb:X6}",
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x80, 0x80, 0x80)),
            };
            System.Windows.Automation.AutomationProperties.SetName(swatch, $"Line colour #{rgb:X6}");
            swatch.Click += Swatch_OnClick;
            SwatchPanel.Children.Add(swatch);
        }
    }

    private void Swatch_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Color color })
        {
            Edit(s => s.BorderColor = new ArgbColor(0xFF, color.R, color.G, color.B));
        }
    }

    private void DashedCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        var dashed = DashedCheckBox.IsChecked == true;
        Edit(s => s.Dashed = dashed);
    }

    /// <summary>The length in cm at the drawing scale (what the line stands for), the thickness and the radius in cm on the page.</summary>
    private void RefreshNumbers(Shape shape)
    {
        var scale = _context?.Document.ScaleDenominator ?? 1;
        ThicknessBox.Text = Format(shape.BorderThickness / PxPerCm);
        LengthBox.Text = Format(LineMetrics.Length(shape) * scale / PxPerCm);
        RadiusBox.Text = Format(shape.CornerRadius / PxPerCm);
        DepthBox.Text = shape.Depth3D.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private void NumberBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyNumber(sender);
            e.Handled = true;
        }
    }

    private void NumberBox_OnLostFocus(object sender, RoutedEventArgs e) => ApplyNumber(sender);

    /// <summary>Reads one of the number boxes and applies it as one undo step; a wrong entry goes back to the current value.</summary>
    private void ApplyNumber(object sender)
    {
        if (_syncing || _shape is not { } shape || _context is null || sender is not TextBox { Tag: string field } box)
        {
            return;
        }

        if (!double.TryParse(box.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            RefreshNumbers(shape);
            return;
        }

        var scale = _context.Document.ScaleDenominator;
        switch (field)
        {
            case "Thickness":
                var thickness = Math.Clamp(value, 0.005, 20) * PxPerCm;
                if (Math.Abs(thickness - shape.BorderThickness) > 1e-6)
                {
                    Edit(s => s.BorderThickness = thickness);
                }

                break;
            case "Length":
                var length = value / scale * PxPerCm;
                if (value > 0 && Math.Abs(length - LineMetrics.Length(shape)) > 1e-6)
                {
                    Edit(s => LineMetrics.SetLength(s, length));
                }

                break;
            case "Radius":
                var radius = Math.Min(value, 20) * PxPerCm;
                if (Math.Abs(radius - shape.CornerRadius) > 1e-6)
                {
                    Edit(s => s.CornerRadius = radius);
                }

                break;
            default:
                var depth = Math.Min(value, 60);
                if (Math.Abs(depth - shape.Depth3D) > 1e-6)
                {
                    Edit(s => s.Depth3D = depth);
                }

                break;
        }

        _syncing = true;
        try
        {
            RefreshNumbers(shape);
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>Shows the current numbers again after the shape was changed elsewhere (a dragged handle, the right panel), unless one is being typed.</summary>
    public void RefreshValues()
    {
        if (_shape is null || _syncing || Visibility != Visibility.Visible || NumbersGrid.IsKeyboardFocusWithin)
        {
            return;
        }

        _syncing = true;
        try
        {
            RefreshNumbers(_shape);
            DashedCheckBox.IsChecked = _shape.Dashed;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void Edit(Action<Shape> change)
    {
        if (_syncing || _shape is null || _context is null || _shape.Locked)
        {
            return;
        }

        var before = ShapeSnapshot.Capture(_shape);
        change(_shape);
        var after = ShapeSnapshot.Capture(_shape);

        if (!after.Equals(before))
        {
            _context.UndoManager.Push(UndoableAction.ForPropertyEdit([_shape], [before], [after]));
        }

        _context.RequestRedraw();
    }
}
