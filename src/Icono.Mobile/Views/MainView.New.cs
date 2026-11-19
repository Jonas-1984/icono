using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Icono.Core;
using Icono.IO;

namespace Icono.Mobile.Views;

/// <summary>"New": a window like the one of the Windows program - a preset for the page (A4, A5, an icon, full HD or its own size in px) and whether the page starts
/// white or transparent.</summary>
public partial class MainView
{
    private static readonly (string Label, int Width, int Height)[] NewPresets =
    [
        ("A4 portrait", Document.A4Width, Document.A4Height),
        ("A4 landscape", Document.A4Height, Document.A4Width),
        ("A5 portrait", 559, 794),
        ("Icon 256 x 256", 256, 256),
        ("Icon 512 x 512", 512, 512),
        ("Icon 1024 x 1024", 1024, 1024),
        ("Full HD 1920 x 1080", 1920, 1080),
        ("Own size", 0, 0),
    ];

    private int _newPreset;
    private bool _newTransparent;

    private void SetUpNew()
    {
        ExportCropSwitch.IsCheckedChanged += (_, _) =>
        {
            if (ExportLayer.IsVisible)
            {
                UpdateExport();
            }
        };

        ExportRangeEntry.TextChanged += (_, _) =>
        {
            if (ExportLayer.IsVisible)
            {
                UpdateExport();
            }
        };

        NewWidthEntry.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                NewHeightEntry.Focus();
            }
        };
        NewHeightEntry.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                OnNewCreate(this, new RoutedEventArgs());
            }
        };
    }

    private void OpenNewDialog()
    {
        _newPreset = 0;
        _newTransparent = false;
        ApplyNewPreset();
        NewLayer.IsVisible = true;
    }

    private void CloseNewDialog() => NewLayer.IsVisible = false;

    private bool IsNewOpen => NewLayer.IsVisible;

    /// <summary>Fills in the size of the chosen preset and marks the keys.</summary>
    private void ApplyNewPreset()
    {
        NewPresetGrid.Children.Clear();
        for (var i = 0; i < NewPresets.Length; i++)
        {
            var index = i;
            var key = new Button { Content = NewPresets[i].Label, Classes = { "tool" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            key.Classes.Set("on", i == _newPreset);
            key.Click += (_, _) =>
            {
                _newPreset = index;
                ApplyNewPreset();
            };
            NewPresetGrid.Children.Add(key);
        }

        var preset = NewPresets[_newPreset];
        if (preset.Width > 0)
        {
            NewWidthEntry.Text = preset.Width.ToString(CultureInfo.InvariantCulture);
            NewHeightEntry.Text = preset.Height.ToString(CultureInfo.InvariantCulture);
            NewSizeHint.Text = $"{preset.Width / 96.0 * 25.4:0.#} x {preset.Height / 96.0 * 25.4:0.#} mm at 96 dpi";
        }
        else
        {
            NewSizeHint.Text = "Type the width and the height in px (1 to 10000).";
        }

        NewWhiteButton.Classes.Set("on", !_newTransparent);
        NewTransparentButton.Classes.Set("on", _newTransparent);
    }

    private void OnNewWhite(object? sender, RoutedEventArgs e)
    {
        _newTransparent = false;
        ApplyNewPreset();
    }

    private void OnNewTransparent(object? sender, RoutedEventArgs e)
    {
        _newTransparent = true;
        ApplyNewPreset();
    }

    private void OnNewCancel(object? sender, RoutedEventArgs e) => CloseNewDialog();

    private void OnNewCreate(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(NewWidthEntry.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(NewHeightEntry.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            || width is < 1 or > 10000 || height is < 1 or > 10000)
        {
            NewSizeHint.Text = "Enter a width and a height between 1 and 10000 px.";
            return;
        }

        var document = new Document
        {
            Width = width,
            Height = height,
            BackgroundMode = _newTransparent ? BackgroundMode.Transparent : BackgroundMode.SolidColor,
            ScaleDenominator = DefaultScale,
        };
        document.Layers.Add(new Layer { Name = "Layer 1" });

        CloseNewDialog();
        LoadDocument(DocumentFile.From(document), null, "Untitled");
        StatusText.Text = $"New drawing ({width} x {height} px, scale 1:{DefaultScale})";
    }
}
