using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

public static class DocumentRenderer
{
    public static void Render(
        SKCanvas canvas, Document document, int width, int height, Shape? pending = null, bool forExport = false,
        bool includeBackground = false)
    {
        // Texts written while rendering (dimensions, wall lengths) use the scale of THIS document; the previous one is put back afterwards
        // so rendering another document (export, thumbnails) never changes what the live canvas shows.
        var previousScale = DrawingScale.Denominator;
        DrawingScale.Denominator = document.ScaleDenominator;
        try
        {
            RenderCore(canvas, document, width, height, pending, forExport, includeBackground);
        }
        finally
        {
            DrawingScale.Denominator = previousScale;
        }
    }

    private static void RenderCore(
        SKCanvas canvas, Document document, int width, int height, Shape? pending, bool forExport, bool includeBackground)
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

        using var wallJoins = WallJoiner.Begin(document);

        // symbols (doors, windows) find the wall they sit in through the document being drawn
        var previousDocument = SymbolMeasurements.Current;
        SymbolMeasurements.Current = document;
        try
        {
            DrawLayers(canvas, document, pending);
        }
        finally
        {
            SymbolMeasurements.Current = previousDocument;
        }

        SheetRenderer.Draw(canvas, document);
    }

    private static void DrawLayers(SKCanvas canvas, Document document, Shape? pending)
    {
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
