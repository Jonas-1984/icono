using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Soft frosted-glass preview background — a contrast aid while editing, never exported.
/// Fully user-configurable: base color, optional gradient, blur strength, edge fade, and overall
/// opacity (see the Document.BackgroundBlur* properties).</summary>
public static class BlurBackgroundRenderer
{
    public static void Render(SKCanvas canvas, int width, int height, Document document)
    {
        canvas.Clear(new SKColor(0x2B, 0x2B, 0x2B));

        var opacity = (byte)Math.Clamp(document.BackgroundOpacity / 100.0 * 255.0, 0, 255);
        var baseColor = document.BackgroundBlurColor.ToSkColor().WithAlpha(opacity);
        var blurRadius = (float)Math.Clamp(document.BackgroundBlurAmount / 100.0 * 40.0, 0, 100);
        var fade = (float)Math.Clamp(document.BackgroundFadeAmount / 100.0, 0, 1);

        using var blur = blurRadius > 0 ? SKImageFilter.CreateBlur(blurRadius, blurRadius) : null;
        using var layerPaint = new SKPaint { ImageFilter = blur };
        canvas.SaveLayer(layerPaint);

        using var gradientShader = document.BackgroundBlurGradientEnabled
            ? SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(width, height),
                [baseColor, document.BackgroundBlurGradientColor.ToSkColor().WithAlpha(opacity)],
                (float[]?)null,
                SKShaderTileMode.Clamp)
            : null;

        using var fillPaint = new SKPaint { IsAntialias = true, Color = baseColor, Shader = gradientShader };
        canvas.DrawRect(SKRect.Create(0, 0, width, height), fillPaint);

        if (fade > 0)
        {
            var radius = Math.Max(width, height) * 0.7f;
            using var fadeShader = SKShader.CreateRadialGradient(
                new SKPoint(width / 2f, height / 2f),
                radius,
                [new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, (byte)(fade * 255))],
                [0f, 1f],
                SKShaderTileMode.Clamp);
            using var fadePaint = new SKPaint { Shader = fadeShader, BlendMode = SKBlendMode.DstOut };
            canvas.DrawRect(SKRect.Create(0, 0, width, height), fadePaint);
        }

        canvas.Restore();
    }
}
