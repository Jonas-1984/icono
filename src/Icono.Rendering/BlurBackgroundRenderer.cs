using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Soft frosted-glass preview background — a contrast aid while editing, never exported.</summary>
public static class BlurBackgroundRenderer
{
    public static void Render(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(new SKColor(0xEE, 0xEE, 0xEE));

        using var blur = SKImageFilter.CreateBlur(24f, 24f);
        using var layerPaint = new SKPaint { ImageFilter = blur };
        canvas.SaveLayer(layerPaint);

        DrawBlob(canvas, width * 0.25f, height * 0.20f, width * 0.50f, new SKColor(0x6F, 0xB1, 0xFF, 160));
        DrawBlob(canvas, width * 0.80f, height * 0.75f, width * 0.55f, new SKColor(0xFF, 0x8A, 0x65, 140));
        DrawBlob(canvas, width * 0.20f, height * 0.85f, width * 0.40f, new SKColor(0xB3, 0x88, 0xFF, 120));

        canvas.Restore();
    }

    private static void DrawBlob(SKCanvas canvas, float cx, float cy, float r, SKColor color)
    {
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = color };
        canvas.DrawCircle(cx, cy, r, paint);
    }
}
