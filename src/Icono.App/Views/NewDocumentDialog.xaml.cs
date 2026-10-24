using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Icono.Core;

namespace Icono.App.Views;

/// <summary>"File > New": pick a page preset (or any size) and whether the page starts white or transparent.</summary>
public partial class NewDocumentDialog : ThemedDialogWindow
{
    private sealed record Preset(string Label, int Width, int Height);

    private static readonly Preset[] Presets =
    [
        new("A4 portrait (210 x 297 mm)", Document.A4Width, Document.A4Height),
        new("A4 landscape (297 x 210 mm)", Document.A4Height, Document.A4Width),
        new("A5 portrait (148 x 210 mm)", 559, 794),
        new("Icon 256 x 256", 256, 256),
        new("Icon 512 x 512", 512, 512),
        new("Icon 1024 x 1024", 1024, 1024),
        new("Full HD 1920 x 1080", 1920, 1080),
        new("Custom", 0, 0),
    ];

    public int PageWidth { get; private set; } = Document.A4Width;

    public int PageHeight { get; private set; } = Document.A4Height;

    public bool TransparentBackground => TransparentRadio.IsChecked == true;

    public NewDocumentDialog()
    {
        InitializeComponent();
        PresetComboBox.ItemsSource = Presets;
        PresetComboBox.SelectedIndex = 0;
    }

    private void PresetComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetComboBox.SelectedItem is not Preset { Width: > 0 } preset)
        {
            WidthTextBox.Focus();
            return;
        }

        WidthTextBox.Text = preset.Width.ToString(CultureInfo.InvariantCulture);
        HeightTextBox.Text = preset.Height.ToString(CultureInfo.InvariantCulture);
        SizeHintText.Text = $"{preset.Width / DesignGridMath.PxPerMm:0.#} x {preset.Height / DesignGridMath.PxPerMm:0.#} mm at 96 DPI";
    }

    private void CreateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(WidthTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(HeightTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) ||
            width is < 1 or > 10000 || height is < 1 or > 10000)
        {
            SizeHintText.Text = "Enter a width and height between 1 and 10000 px.";
            return;
        }

        PageWidth = width;
        PageHeight = height;
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
