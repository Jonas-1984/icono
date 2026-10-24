using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

public static class DocumentRenderer
{
    public static void Render(
        SKCanvas canvas, Document document, int width, int height, Shape? pending = null, bool forExport = false,
        bool includeBackground = false)
    {
        if (forExport)
        {
            // The checkerboard of the "Transparent" mode is only a preview aid. An export is transparent unless the
            // caller asks for the page background (colour / blur / picture) to be part of the image.
            canvas.Clear(SKColors.Transparent);

            if (includeBackground && document.BackgroundMode != BackgroundMode.Transparent)
            {
                canvas.Save();
                canvas.ClipRect(SKRect.Create(0, 0, width, height));
                switch (document.BackgroundMode)
                {
                    case BackgroundMode.SolidColor:
                        canvas.Clear(document.BackgroundColor.ToSkColor());
                        break;
                    case BackgroundMode.Blur:
                        BlurBackgroundRenderer.Render(canvas, width, height, document);
                        break;
                    case BackgroundMode.Image:
                        BackgroundImageRenderer.Render(canvas, width, height, document);
                        break;
                }

                canvas.Restore();
            }
        }
        else
        {
            switch (document.BackgroundMode)
            {
                case BackgroundMode.SolidColor:
                    canvas.Clear(document.BackgroundColor.ToSkColor());
                    break;
                case BackgroundMode.Blur:
                    BlurBackgroundRenderer.Render(canvas, width, height, document);
                    break;
                case BackgroundMode.Image:
                    BackgroundImageRenderer.Render(canvas, width, height, document);
                    break;
                default:
                    CheckerboardRenderer.Render(canvas, width, height);
                    break;
            }
        }

        for (var i = document.Layers.Count - 1; i >= 0; i--)
        {
            var layer = document.Layers[i];
            if (!layer.Visible)
            {
                continue;
            }

            for (var j = layer.Shapes.Count - 1; j >= 0; j--)
            {
                ShapeRenderer.Draw(canvas, layer.Shapes[j]);
            }
        }

        if (pending is not null)
        {
            ShapeRenderer.Draw(canvas, pending);
        }
    }
}
