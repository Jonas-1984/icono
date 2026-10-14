using System.Runtime.CompilerServices;
using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Draws the imported page-background picture (<see cref="BackgroundMode.Image"/>). The decoded
/// bitmap is cached per byte array, since the canvas repaints on every mouse move.</summary>
public static class BackgroundImageRenderer
{
    private static readonly ConditionalWeakTable<byte[], SKImage?> Cache = new();

    public static void Render(SKCanvas canvas, int width, int height, Document document)
    {
        // The page colour shows behind the picture (letterbox bars for Contain, or while no picture is set).
        canvas.Clear(document.BackgroundColor.ToSkColor());

        var bytes = document.BackgroundImageBytes;
        if (bytes is not { Length: > 0 })
        {
            return;
        }

        var image = Cache.GetValue(bytes, static b => SKImage.FromEncodedData(b));
        if (image is null || image.Width <= 0 || image.Height <= 0)
        {
            return;
        }

        var page = SKRect.Create(0, 0, width, height);
        var source = SKRect.Create(0, 0, image.Width, image.Height);
        SKRect dest;

        switch (document.BackgroundImageFit)
        {
            case BackgroundImageFit.Stretch:
                dest = page;
                break;

            case BackgroundImageFit.Contain:
            {
                var scale = Math.Min(width / (float)image.Width, height / (float)image.Height);
                var w = image.Width * scale;
                var h = image.Height * scale;
                dest = SKRect.Create((width - w) / 2, (height - h) / 2, w, h);
                break;
            }

            default:
            {
                // Cover: crop the source so the scaled picture exactly fills the page.
                var scale = Math.Max(width / (float)image.Width, height / (float)image.Height);
                var visibleW = width / scale;
                var visibleH = height / scale;
                source = SKRect.Create((image.Width - visibleW) / 2, (image.Height - visibleH) / 2, visibleW, visibleH);
                dest = page;
                break;
            }
        }

        var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(image, source, dest, sampling, paint);
    }
}
