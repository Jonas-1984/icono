using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

public static class DocumentRenderer
{
    public static void Render(
        SKCanvas canvas, Document document, int width, int height, Shape? pending = null, bool forExport = false)
    {
        if (forExport)
        {
            // Background modes are a preview aid only — exported output is always transparent.
            canvas.Clear(SKColors.Transparent);
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
