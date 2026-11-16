using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Icono.Core;
using Icono.IO;

namespace Icono.Mobile.Views;

public partial class MainView : UserControl
{
    /// <summary>The scale a new drawing starts with (same as the Windows program, MainWindow.DefaultScale).</summary>
    private const double DefaultScale = 40;

    public MainView()
    {
        InitializeComponent();
        Canvas.ZoomChanged += (_, _) => ZoomText.Text = $"{Canvas.Zoom * 100:0} %";
        Canvas.Document = NewDocument();
    }

    /// <summary>An empty A4 page, as the Windows program starts.</summary>
    private static Document NewDocument()
    {
        var document = new Document
        {
            Width = Document.A4Width,
            Height = Document.A4Height,
            BackgroundMode = BackgroundMode.SolidColor,
            ScaleDenominator = DefaultScale,
        };
        document.Layers.Add(new Layer { Name = "Layer 1" });
        return document;
    }

    private void OnNew(object? sender, RoutedEventArgs e)
    {
        Canvas.Document = NewDocument();
        TitleText.Text = "Untitled";
        StatusText.Text = "New page (A4, scale 1:" + DefaultScale + ")";
    }

    private void OnFit(object? sender, RoutedEventArgs e) => Canvas.FitToView();

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null || !storage.CanOpen)
        {
            StatusText.Text = "This device cannot open files.";
            return;
        }

        // .icn has no MIME type Android knows, so every file is offered and the content decides
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open an icono drawing",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("icono drawing") { Patterns = ["*.icn", "*.icono"], MimeTypes = ["*/*"] }],
        });
        if (files.Count == 0)
        {
            return;
        }

        var picked = files[0];
        try
        {
            StatusText.Text = "Opening " + picked.Name + " ...";
            await using var stream = await picked.OpenReadAsync();
            var file = await Task.Run(() => DocumentFile.Load(stream));
            var document = new Document();
            file.ApplyTo(document);
            Canvas.Document = document;
            TitleText.Text = picked.Name;
            var shapes = document.Layers.Sum(l => l.Shapes.Count);
            StatusText.Text = $"{picked.Name}: {document.PageCount} page(s), {shapes} object(s), scale 1:{document.ScaleDenominator:0.##}";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = picked.Name + " could not be opened: " + ex.Message;
        }
    }
}
