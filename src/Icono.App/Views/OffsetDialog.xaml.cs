using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace Icono.App.Views;

/// <summary>Modal dialog prompting for the signed offset distance used by
/// <see cref="Icono.App.State.EditorContext.OffsetSelection"/> — see <see cref="Icono.Rendering.PathOffsetter"/>
/// for the actual geometry.</summary>
public partial class OffsetDialog : Window
{
    public double Distance { get; private set; }

    public OffsetDialog()
    {
        InitializeComponent();
        DistanceTextBox.Focus();
        DistanceTextBox.SelectAll();
    }

    private void ApplyButton_OnClick(object sender, RoutedEventArgs e) => TryApply();

    private void DistanceTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryApply();
        }
    }

    private void TryApply()
    {
        if (!double.TryParse(DistanceTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) || value == 0)
        {
            return;
        }

        Distance = value;
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
