using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Icono.Core;
using Icono.Editor;
using Icono.Rendering;

namespace Icono.Mobile.Views;

/// <summary>"Picture" in the top bar: a picture (PNG, JPEG, WebP ...) or an SVG from the tablet goes into the middle of the page
/// (<see cref="ImageImport"/>, shared with the Windows program). A picture becomes one image object, an SVG becomes shapes that can be coloured.</summary>
public partial class MainView
{
    private static readonly string[] PictureExtensions = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".ico", ".svg"];

    private static readonly FilePickerFileType PictureFiles = new("Pictures and SVG")
    {
        Patterns = PictureExtensions.Select(e => "*" + e).ToList(),
        MimeTypes = ["image/png", "image/jpeg", "image/webp", "image/gif", "image/bmp", "image/x-icon", "image/vnd.microsoft.icon", "image/svg+xml"],
    };

    private async void OnInsertPicture(object? sender, RoutedEventArgs e)
    {
        if (Canvas.IsDrawingPath)
        {
            Canvas.FinishPath();
        }

        var file = await AskPictureFileAsync();
        if (file is null)
        {
            return;
        }

        var data = await ReadPictureBytesAsync(file);
        if (data is null)
        {
            return;
        }
        string? error;
        if (file.Name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            error = ImageImport.ImportSvg(_context, System.Text.Encoding.UTF8.GetString(data), out var failed) is null ? failed : null;
        }
        else
        {
            error = ImageImport.ImportPicture(_context, data, out var failed) is null ? failed : null;
        }

        StatusText.Text = error ?? "Added " + file.Name + " - drag it to move it, its corners to change the size.";
    }

    /// <summary>"SVG code..." of the picture window: the code of an SVG (or only path data) is pasted into a text box and becomes shapes, as the Insert > SVG Code menu
    /// of the Windows program does.</summary>
    private void OnFilesSvgCode(object? sender, RoutedEventArgs e)
    {
        CloseFiles(null);
        OpenTextBox("SVG code", "Paste the code of an SVG (or only the path data). It becomes shapes in the middle of the page.", string.Empty, multiline: true, text =>
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            StatusText.Text = ImageImport.ImportSvg(_context, text, out var error) is null
                ? error
                : "Added the SVG shapes - drag them to move them, their corners to change the size.";
        });
    }

    /// <summary>Reads the file chosen in the file window (a status message tells when it is too big or cannot be read).</summary>
    private async Task<byte[]?> ReadPictureBytesAsync(IStorageFile file)
    {
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            var limit = file.Name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? SvgImporter.MaxFileBytes : ImageImport.MaxPictureBytes;
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                memory.Write(buffer, 0, read);
                if (memory.Length > limit)
                {
                    StatusText.Text = "That file is too big.";
                    return null;
                }
            }

            return memory.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText.Text = "The file could not be read: " + ex.Message;
            return null;
        }
    }

    /// <summary>The picture for the page background (BACKGROUND in the Properties): chosen in the file window, made at most 3000 px.</summary>
    private async void OnBackgroundImageRequested(object? sender, EventArgs e)
    {
        var file = await AskPictureFileAsync();
        if (file is null || file.Name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            if (file is not null)
            {
                StatusText.Text = "A background needs a picture (PNG, JPEG ...), not an SVG.";
            }

            return;
        }

        var data = await ReadPictureBytesAsync(file);
        if (data is null)
        {
            return;
        }

        var png = ImageImport.ToBackgroundPng(data, out var error);
        if (png is null)
        {
            StatusText.Text = error ?? "The file could not be read as an image.";
            return;
        }

        DocumentBackgroundEditing.Edit(_context, d =>
        {
            d.BackgroundImageBytes = png;
            d.BackgroundImageName = file.Name;
            d.BackgroundMode = BackgroundMode.Image;
        });
        StatusText.Text = "Background: " + file.Name;
    }
}
