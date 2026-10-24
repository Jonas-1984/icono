using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Icono.App.Views;

/// <summary>Modal dialog for pasting SVG markup (or bare path "d" data) to import as one or more
/// shapes — see <see cref="Icono.Rendering.SvgImporter"/> for the actual parsing.</summary>
public partial class ImportSvgDialog : ThemedDialogWindow
{
    public string SvgCode => CodeTextBox.Text;

    public ImportSvgDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => CodeTextBox.Focus();
    }

    private void CodeTextBox_OnTextChanged(object sender, TextChangedEventArgs e) =>
        ImportButton.IsEnabled = !string.IsNullOrWhiteSpace(CodeTextBox.Text);

    private void LoadFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "SVG files (*.svg)|*.svg|All files (*.*)|*.*", Title = "Load SVG file" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            CodeTextBox.Text = File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThemedMessageBox.Show(this, "The file could not be read.", "Import SVG", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
