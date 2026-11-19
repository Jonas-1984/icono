using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Icono.Core;
using Icono.Rendering;

namespace Icono.Mobile.Views;

/// <summary>Export: the drawing as a PDF (every page at its real paper size, in vectors - printed at 100 % it keeps the drawing scale) or as a
/// PNG picture (one page, or all pages below each other), saved to a file or shared with another app. The files are made by the shared
/// <see cref="DocumentExporter"/>.</summary>
public partial class MainView
{
    /// <summary>The longest side a PNG may have (a picture of all pages at 300 dpi would need hundreds of megabytes).</summary>
    private const int MaxPngSide = 12000;

    private enum ExportKind
    {
        Pdf,
        Png,
        Svg,
        Ico,
        PngSet,
    }

    private ExportKind _exportKind = ExportKind.Pdf;
    private bool _exportAllPages;
    private bool _exportSome; // the pages typed in the box (PDF and PNG set)
    private int _exportDpi = 150;

    /// <summary>True when only the drawing is exported (cut out, on a transparent background): not for a PDF, which is a sheet of paper.</summary>
    private bool OnlyDrawing => ExportCropSwitch.IsChecked == true && _exportKind != ExportKind.Pdf;

    private void OnExport(object? sender, RoutedEventArgs e)
    {
        _exportAllPages = _context.Document.PageCount > 1 && _exportAllPages;
        if (string.IsNullOrWhiteSpace(ExportRangeEntry.Text))
        {
            ExportRangeEntry.Text = (_context.CurrentPage + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        ExportShareButton.IsVisible = Sharing.ShareFile is not null;
        ExportPrintButton.IsVisible = Sharing.PrintPdf is not null;
        UpdateExport();
        ExportLayer.IsVisible = true;
    }

    private void UpdateExport()
    {
        var document = _context.Document;
        var page = _context.CurrentPage;
        var several = document.PageCount > 1;
        if (!several)
        {
            _exportAllPages = false;
        }

        ExportPdfButton.Classes.Set("on", _exportKind == ExportKind.Pdf);
        ExportPngButton.Classes.Set("on", _exportKind == ExportKind.Png);
        ExportSvgButton.Classes.Set("on", _exportKind == ExportKind.Svg);
        ExportIcoButton.Classes.Set("on", _exportKind == ExportKind.Ico);
        ExportSetButton.Classes.Set("on", _exportKind == ExportKind.PngSet);
        ExportCurrentButton.Content = several ? $"Page {page + 1}" : "The page";
        ExportAllButton.Content = $"All {document.PageCount} pages";
        ExportAllButton.IsVisible = several && _exportKind != ExportKind.Ico; // an icon is made of one page
        var someUsable = several && (_exportKind is ExportKind.Pdf or ExportKind.PngSet); // one file can hold several pages only as a PDF or in a zip
        if (!someUsable)
        {
            _exportSome = false;
        }

        ExportSomeButton.IsVisible = someUsable;
        ExportRangeEntry.IsVisible = _exportSome;
        ExportCurrentButton.Classes.Set("on", !_exportAllPages && !_exportSome);
        ExportAllButton.Classes.Set("on", _exportAllPages);
        ExportSomeButton.Classes.Set("on", _exportSome);
        ExportResolutionPanel.IsVisible = _exportKind == ExportKind.Png;
        ExportCropRow.IsVisible = _exportKind != ExportKind.Pdf;
        ExportBackground.IsEnabled = !OnlyDrawing; // only the drawing has no page behind it
        ExportDpi96.Classes.Set("on", _exportDpi == 96);
        ExportDpi150.Classes.Set("on", _exportDpi == 150);
        ExportDpi300.Classes.Set("on", _exportDpi == 300);

        var mm = (Width: document.Width / 96.0 * 25.4, Height: document.Height / 96.0 * 25.4);
        var thePage = several ? $"page {page + 1}" : "the page";
        if (_exportKind == ExportKind.Svg)
        {
            ExportInfo.Text = $"A vector graphic (.svg) of {(_exportAllPages ? "all pages below each other" : thePage)}: lines stay sharp at any size, text becomes outlines.";
        }
        else if (_exportKind == ExportKind.Ico)
        {
            ExportInfo.Text = $"A Windows icon (.ico) of {thePage} with the sizes 16, 24, 32, 48, 64, 128 and 256 px. The page is fitted into square frames.";
        }
        else if (_exportKind == ExportKind.PngSet)
        {
            ExportInfo.Text = $"A zip file with six pictures (16, 32, 64, 128, 256 and 512 px on the longer side) of {(_exportAllPages ? "every page" : _exportSome ? "the pages you typed" : thePage)}.";
        }
        else if (_exportKind == ExportKind.Pdf)
        {
            var pages = _exportAllPages ? document.PageCount : _exportSome ? (PageRange.TryParse(ExportRangeEntry.Text, document.PageCount, out var typed, out var typedError) ? typed.Count : 0) : 1;
            if (_exportSome && pages == 0)
            {
                ExportInfo.Text = PageRange.TryParse(ExportRangeEntry.Text, document.PageCount, out _, out var why) ? "" : why;
                return;
            }


            ExportInfo.Text = $"{pages} PDF page(s) of {mm.Width:0} x {mm.Height:0} mm. Printed at 100 % (no \"fit to page\") the drawing keeps " +
                              $"its scale 1:{document.ScaleDenominator:0.##}.";
        }
        else
        {
            var (width, height) = PngSize();
            ExportInfo.Text = $"A picture of {width} x {height} pixels" +
                              (_exportAllPages ? " with all pages below each other." : ".") +
                              (Math.Max(width, height) >= MaxPngSide ? " (limited - choose a lower resolution or one page for a sharper picture)" : "");
        }

        if (OnlyDrawing)
        {
            try
            {
                var area = DocumentExporter.DrawnArea(document, _exportAllPages ? null : page);
                ExportInfo.Text += $" Only what is drawn ({area.Width:0} x {area.Height:0} px of the page), on a transparent background.";
            }
            catch (InvalidOperationException ex)
            {
                ExportInfo.Text = ex.Message; // nothing is drawn
            }
        }
    }

    /// <summary>The size of what is exported before it is scaled: the page (or all pages below each other), or only the drawing.</summary>
    private (int Width, int Height) ExportExtent()
    {
        var page = _exportAllPages ? (int?)null : _context.CurrentPage;
        if (OnlyDrawing)
        {
            try
            {
                var area = DocumentExporter.DrawnArea(_context.Document, page);
                return ((int)Math.Ceiling(area.Width), (int)Math.Ceiling(area.Height));
            }
            catch (InvalidOperationException)
            {
                // nothing is drawn: the message is shown, the page size gives the numbers
            }
        }

        return DocumentExporter.Extent(_context.Document, page);
    }

    private (int Width, int Height) PngSize()
    {
        var (width, height) = ExportExtent();
        var scale = Math.Min(_exportDpi / 96.0, MaxPngSide / (double)Math.Max(width, height));
        return ((int)Math.Round(width * scale), (int)Math.Round(height * scale));
    }

    private void OnExportFormat(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<ExportKind>(tag, out var kind))
        {
            SetExport(() =>
            {
                _exportKind = kind;
                if (kind == ExportKind.Ico)
                {
                    _exportAllPages = false;
                }
            });
        }
    }

    private void OnExportCurrentPage(object? sender, RoutedEventArgs e) => SetExport(() => (_exportAllPages, _exportSome) = (false, false));

    private void OnExportAllPages(object? sender, RoutedEventArgs e) => SetExport(() => (_exportAllPages, _exportSome) = (true, false));

    private void OnExportSomePages(object? sender, RoutedEventArgs e)
    {
        SetExport(() => (_exportAllPages, _exportSome) = (false, true));
        ExportRangeEntry.Focus(Avalonia.Input.NavigationMethod.Tab);
        ExportRangeEntry.SelectAll();
    }

    /// <summary>The pages typed in the box, or false (the message is shown in the window, which stays open) when they are not right.</summary>
    private bool TryExportPages(out List<int> pages)
    {
        if (PageRange.TryParse(ExportRangeEntry.Text, _context.Document.PageCount, out pages, out var error))
        {
            return true;
        }

        ExportInfo.Text = error;
        ExportRangeEntry.Focus();
        return false;
    }

    /// <summary>The pages of a PDF: the ones typed, all, or the page being worked on (null = all).</summary>
    private IReadOnlyList<int>? ExportPdfPages(bool allPages, int page) => _exportSome && TryExportPages(out var typed) ? typed : allPages ? null : [page];

    private void OnExportDpi(object? sender, RoutedEventArgs e) =>
        SetExport(() => _exportDpi = sender is Button { Tag: string tag } && int.TryParse(tag, out var dpi) ? dpi : 150);

    private void SetExport(Action change)
    {
        change();
        UpdateExport();
    }

    private void OnExportCancel(object? sender, RoutedEventArgs e) => ExportLayer.IsVisible = false;

    /// <summary>The file to write: its name, MIME type and content (made off the UI thread).</summary>
    private async Task<(string Name, string Mime, byte[] Content)> MakeExportAsync()
    {
        var document = _context.Document;
        var background = ExportBackground.IsChecked == true;
        var page = _context.CurrentPage;
        var allPages = _exportAllPages;
        var onlyDrawing = OnlyDrawing;
        var baseName = Path.GetFileNameWithoutExtension(_name);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "Drawing";
        }

        var pageSuffix = _exportSome ? "-pages" : allPages || document.PageCount == 1 ? "" : $"-page{page + 1}";
        var chosen = ExportPdfPages(allPages, page); // the pages of a PDF or of a PNG set
        switch (_exportKind)
        {
            case ExportKind.Svg:
            {
                var svg = await Task.Run(() => DocumentExporter.RenderSvg(document, background, allPages ? null : page, onlyDrawing));
                return (baseName + pageSuffix + ".svg", "image/svg+xml", svg);
            }

            case ExportKind.Ico:
            {
                var ico = await Task.Run(() => DocumentExporter.RenderIco(document, DocumentExporter.IconSizes, background, page, onlyDrawing));
                return (baseName + pageSuffix + ".ico", "image/vnd.microsoft.icon", ico);
            }

            case ExportKind.PngSet:
            {
                var zip = await Task.Run(() => chosen is { } list ? DocumentExporter.RenderPngSetZip(document, baseName, background, list, onlyDrawing) : DocumentExporter.RenderPngSetZip(document, baseName, background, page: null, onlyDrawing: onlyDrawing));
                return (baseName + pageSuffix + ".zip", "application/zip", zip);
            }
        }

        if (_exportKind == ExportKind.Pdf)
        {
            var pdf = await Task.Run(() => DocumentExporter.RenderPdf(document, background, chosen));
            return (baseName + pageSuffix + ".pdf", "application/pdf", pdf);
        }

        var (width, height) = PngSize();
        var png = await Task.Run(() => DocumentExporter.RenderPng(document, Math.Max(width, height), background, allPages ? null : page, onlyDrawing));
        return (baseName + pageSuffix + ".png", "image/png", png);
    }

    /// <summary>"Print...": the pages as a PDF (always a PDF, whatever format is chosen above, so the paper size and the scale are right) go to the print dialog of the
    /// tablet - as File > Print in the Windows program.</summary>
    private async void OnExportPrint(object? sender, RoutedEventArgs e)
    {
        if (Sharing.PrintPdf is not { } print)
        {
            return;
        }

        if (_exportSome && !TryExportPages(out _))
        {
            return;
        }

        ExportLayer.IsVisible = false;
        StatusText.Text = "Preparing the print ...";
        var document = _context.Document;
        var background = ExportBackground.IsChecked == true;
        var page = _context.CurrentPage;
        var allPages = _exportAllPages;
        var name = Path.GetFileNameWithoutExtension(_name);
        var printPages = ExportPdfPages(allPages, page);
        var pdf = await Task.Run(() => DocumentExporter.RenderPdf(document, background, printPages));
        StatusText.Text = print(string.IsNullOrWhiteSpace(name) ? "icono drawing" : name, pdf)
            ? "The print dialog is open."
            : "The tablet could not open its print dialog.";
    }

    private async void OnExportSave(object? sender, RoutedEventArgs e)
    {
        if (_exportSome && !TryExportPages(out _))
        {
            return;
        }

        ExportLayer.IsVisible = false;
        StatusText.Text = "Exporting ...";
        try
        {
            var (name, mime, content) = await MakeExportAsync();

            // icono's own file window (with Cancel); the Android one only for "Other location..."
            var target = await AskSaveFileAsync(
                _exportKind switch { ExportKind.Pdf => "Save the PDF", ExportKind.Svg => "Save the vector graphic", ExportKind.Ico => "Save the icon", ExportKind.PngSet => "Save the pictures", _ => "Save the picture" },
                Path.GetFileNameWithoutExtension(name),
                Path.GetExtension(name),
                new FilePickerFileType(_exportKind switch { ExportKind.Pdf => "PDF document", ExportKind.Svg => "SVG graphic", ExportKind.Ico => "Windows icon", ExportKind.PngSet => "Zip file", _ => "PNG picture" }) { Patterns = ["*" + Path.GetExtension(name)], MimeTypes = [mime] });
            if (target is null)
            {
                StatusText.Text = "Export cancelled.";
                return;
            }

            await using (var stream = await target.OpenWriteAsync())
            {
                if (stream.CanSeek)
                {
                    stream.SetLength(0);
                }

                await stream.WriteAsync(content);
            }

            StatusText.Text = $"Exported: {target.Name} ({content.Length / 1024.0:0} KB)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText.Text = "The export could not be saved: " + ex.Message;
        }
    }

    private async void OnExportShare(object? sender, RoutedEventArgs e)
    {
        if (Sharing.ShareFile is not { } share)
        {
            return;
        }

        if (_exportSome && !TryExportPages(out _))
        {
            return;
        }

        ExportLayer.IsVisible = false;
        StatusText.Text = "Exporting ...";
        try
        {
            var (name, mime, content) = await MakeExportAsync();
            StatusText.Text = await share(name, mime, content) ? $"Shared: {name}" : "Sharing is not possible on this device - use Save.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            StatusText.Text = "The export could not be made: " + ex.Message;
        }
    }
}
