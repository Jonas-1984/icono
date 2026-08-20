using System.Windows;

namespace Icono.App.Views;

/// <summary>Modal dialog for pasting SVG markup (or bare path "d" data) to import as one or more
/// shapes — see <see cref="Icono.Rendering.SvgImporter"/> for the actual parsing.</summary>
public partial class ImportSvgDialog : Window
{
    public string SvgCode => CodeTextBox.Text;

    public ImportSvgDialog()
    {
        InitializeComponent();
    }

    private void ImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
