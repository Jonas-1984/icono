using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Icono.App.Views;
using Icono.Rendering;

namespace Icono.App;

public partial class MainWindow
{
    /// <summary>Printing at the drawing scale: the page is the paper, so printing it at 100% gives a true 1:N plot.</summary>
    private void PrintMenuItem_OnClick(object sender, RoutedEventArgs e) => PrintDocument();

    private static BitmapImage LoadImage(byte[] png)
    {
        var image = new BitmapImage();
        using (var stream = new MemoryStream(png))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
        }

        image.Freeze();
        return image;
    }

    private void PrintDocument()
    {
        CanvasHost.CommitActiveTextEdit();

        var document = _context.Document;
        var scale = DrawingScale.Label(document.ScaleDenominator);
        var choice = ThemedMessageBox.Choose(
            this, "Print",
            $"The drawing scale is {scale}. Print at actual size so the plot is exactly {scale}, or scaled to fit the paper?",
            [$"Actual size ({scale})", "Fit to paper", "Cancel"], defaultIndex: 0, cancelIndex: 2, MessageBoxImage.Question);
        if (choice is not (0 or 1))
        {
            return;
        }

        var dialog = new PrintDialog();
        try
        {
            // portrait or landscape to suit the page
            dialog.PrintTicket.PageOrientation = document.Width > document.Height
                ? PageOrientation.Landscape
                : PageOrientation.Portrait;
        }
        catch (Exception)
        {
            // some printers expose no ticket until chosen: the dialog still works
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            // The page at 300 DPI; it is laid out in 1/96 inch units, one page pixel being 1/96 inch on the paper.
            var longSide = (int)Math.Ceiling(Math.Max(document.Width, document.Height) / 96.0 * 300);
            // every page of the project is one sheet of paper
            var pageCount = document.PageCount;
            var images = Enumerable.Range(0, pageCount)
                .Select(i => LoadImage(DocumentExporter.RenderPng(document, longSide, includeBackground: true, pageCount > 1 ? i : null)))
                .ToList();

            double width = document.Width;
            double height = document.Height;
            var areaWidth = dialog.PrintableAreaWidth;
            var areaHeight = dialog.PrintableAreaHeight;
            var fits = width <= areaWidth + 0.5 && height <= areaHeight + 0.5;
            var exact = choice == 0 && fits;

            if (!exact)
            {
                var factor = Math.Min(areaWidth / width, areaHeight / height);
                width *= factor;
                height *= factor;
            }

            var left = Math.Max(0, (areaWidth - width) / 2);
            var top = Math.Max(0, (areaHeight - height) / 2);
            var title = $"{DocumentName} ({scale})";
            if (pageCount == 1)
            {
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen())
                {
                    context.DrawImage(images[0], new Rect(left, top, width, height));
                }

                dialog.PrintVisual(visual, title);
            }
            else
            {
                var fixedDocument = new System.Windows.Documents.FixedDocument();
                foreach (var image in images)
                {
                    var sheet = new System.Windows.Documents.FixedPage { Width = areaWidth, Height = areaHeight };
                    var picture = new Image { Source = image, Width = width, Height = height };
                    System.Windows.Documents.FixedPage.SetLeft(picture, left);
                    System.Windows.Documents.FixedPage.SetTop(picture, top);
                    sheet.Children.Add(picture);
                    var content = new System.Windows.Documents.PageContent();
                    ((System.Windows.Markup.IAddChild)content).AddChild(sheet);
                    fixedDocument.Pages.Add(content);
                }

                dialog.PrintDocument(fixedDocument.DocumentPaginator, title);
            }

            ShowStatus(exact || choice == 1
                ? exact ? $"Printed at {scale}" : "Printed scaled to fit the paper"
                : $"The drawing is larger than the paper: printed scaled to fit, so it is no longer exactly {scale}");
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, $"The drawing could not be printed:\n\n{ex.Message}", "Print", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
