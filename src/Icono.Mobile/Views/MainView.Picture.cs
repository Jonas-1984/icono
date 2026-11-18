using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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

        byte[] data;
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
                    return;
                }
            }

            data = memory.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText.Text = "The file could not be read: " + ex.Message;
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
}
