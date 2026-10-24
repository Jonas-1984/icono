using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Icono.App.State;
using Icono.App.State.Undo;
using Icono.Core;

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
    }

    /// <summary>Shows the current values of <paramref name="shape"/> in the controls.</summary>
    public void Bind(EditorContext context, Shape shape)
    {
        _context = context;
        _shape = shape;

        _syncing = true;
        try
        {
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
