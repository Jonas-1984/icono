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

    /// <summary>The page to export (0 = the first), or null for all pages below each other.</summary>
    public int? Page { get; private set; }

    public bool IncludeBackground => BackgroundCheckBox.IsChecked == true;

    public ExportDialog(Document document, bool backgroundDefault)
    {
        _document = document;
        InitializeComponent();

        if (document.PageCount > 1)
        {
            var pages = new List<string> { $"All {document.PageCount} pages (one image)" };
            pages.AddRange(Enumerable.Range(1, document.PageCount).Select(i => $"Page {i}"));
            PageComboBox.ItemsSource = pages;
            PageComboBox.SelectedIndex = 0;
            PagesPanel.Visibility = Visibility.Visible;
        }

        FillSizes();
        SizeComboBox.SelectedIndex = 0;

        BackgroundCheckBox.IsChecked = backgroundDefault;
        UpdateInfo();
    }

    private int? SelectedPage => PageComboBox.SelectedIndex > 0 ? PageComboBox.SelectedIndex - 1 : null;

    private void FillSizes()
    {
        var (width, height) = Icono.Rendering.DocumentExporter.Extent(_document, SelectedPage);
        var pageLongSide = Math.Max(width, height);
        var keep = Math.Max(0, SizeComboBox.SelectedIndex);
        SizeComboBox.ItemsSource = new[]
        {
            new SizeChoice($"Page size ({width} x {height} px)", pageLongSide),
            new SizeChoice("2x page size", pageLongSide * 2),
            new SizeChoice("64 px", 64),
            new SizeChoice("128 px", 128),
            new SizeChoice("256 px", 256),
            new SizeChoice("512 px", 512),
            new SizeChoice("1024 px", 1024),
            new SizeChoice("2048 px", 2048),
        };
        SizeComboBox.SelectedIndex = keep;
    }

    private void PageComboBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SizeComboBox is not null)
        {
            FillSizes();
        }
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
        Page = SelectedPage;
        LongSide = SizeComboBox.SelectedItem is SizeChoice choice ? choice.LongSide : Math.Max(_document.Width, _document.Height);
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
