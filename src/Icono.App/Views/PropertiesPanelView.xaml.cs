using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Icono.App.Views;

public partial class PropertiesPanelView : UserControl
{
    private static readonly Color[] SwatchColors =
    {
        Colors.White, Colors.Black, Colors.Silver,
        Color.FromRgb(0x6F, 0xB1, 0xFF), Color.FromRgb(0x2E, 0x86, 0xFF), Color.FromRgb(0x1B, 0x4F, 0x91),
        Color.FromRgb(0xE5, 0x39, 0x35), Color.FromRgb(0xFB, 0x8C, 0x00), Color.FromRgb(0xFD, 0xD8, 0x35),
        Color.FromRgb(0x43, 0xA0, 0x47), Color.FromRgb(0x8E, 0x24, 0xAA), Color.FromRgb(0x00, 0xAC, 0xC1),
    };

    public PropertiesPanelView()
    {
        InitializeComponent();

        SwatchList.ItemsSource = SwatchColors.Select(c => (Brush)new SolidColorBrush(c)).ToList();

        BorderPresetList.ItemsSource = new List<BorderStylePreset>
        {
            new("Blue Gloss", (Brush)FindResource("GlossyBlueBrush")),
            new("Silver Gloss", new LinearGradientBrush(Colors.White, Color.FromRgb(0x8A, 0x8A, 0x8A), 90)),
            new("Gold Gloss", new LinearGradientBrush(Color.FromRgb(0xFF, 0xE6, 0x8A), Color.FromRgb(0xB8, 0x86, 0x0B), 90)),
            new("Red Gloss", new LinearGradientBrush(Color.FromRgb(0xFF, 0x8A, 0x80), Color.FromRgb(0x8E, 0x1B, 0x1B), 90)),
        };
    }

    private void BorderThicknessSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BorderThicknessValueText is not null)
        {
            BorderThicknessValueText.Text = $"{e.NewValue:0}px";
        }
    }

    private void CornerRadiusSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CornerRadiusValueText is not null)
        {
            CornerRadiusValueText.Text = $"{e.NewValue:0}px";
        }
    }
}
