using System.Windows;
using Icono.Core;

namespace Icono.App.Views;

public enum ExportFormat
{
    Png,
    Ico,
    PngSet,
}

/// <summary>"File > Export": choose PNG / icon / PNG set, the size and whether the page background is exported.</summary>
public partial class ExportDialog : ThemedDialogWindow
{
    private sealed record SizeChoice(string Label, int LongSide);

    private readonly Document _document;

    public ExportFormat Format { get; private set; } = ExportFormat.Png;

    /// <summary>Longer side in px of a single PNG export.</summary>
    public int LongSide { get; private set; }

    public bool IncludeBackground => BackgroundCheckBox.IsChecked == true;

    public ExportDialog(Document document, bool backgroundDefault)
    {
        _document = document;
        InitializeComponent();

        var pageLongSide = Math.Max(document.Width, document.Height);
        SizeComboBox.ItemsSource = new[]
        {
            new SizeChoice($"Page size ({document.Width} x {document.Height} px)", pageLongSide),
            new SizeChoice("2x page size", pageLongSide * 2),
            new SizeChoice("64 px", 64),
            new SizeChoice("128 px", 128),
            new SizeChoice("256 px", 256),
            new SizeChoice("512 px", 512),
            new SizeChoice("1024 px", 1024),
            new SizeChoice("2048 px", 2048),
        };
        SizeComboBox.SelectedIndex = 0;

        BackgroundCheckBox.IsChecked = backgroundDefault;
        UpdateInfo();
    }

    private void FormatRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (SizePanel is null)
        {
            return;
        }

        SizePanel.Visibility = PngRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        if (InfoText is null)
        {
            return;
        }

        InfoText.Text = IcoRadio.IsChecked == true
            ? "The page is fitted into square frames of 16, 24, 32, 48, 64, 128 and 256 px."
            : SetRadio.IsChecked == true
                ? "You pick a file name; one PNG per size is written next to it (name_64.png, name_128.png, ...)."
                : "Exported images are transparent unless the page background is included.";
    }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        Format = IcoRadio.IsChecked == true ? ExportFormat.Ico : SetRadio.IsChecked == true ? ExportFormat.PngSet : ExportFormat.Png;
        LongSide = SizeComboBox.SelectedItem is SizeChoice choice ? choice.LongSide : Math.Max(_document.Width, _document.Height);
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
