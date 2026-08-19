using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

public static class ShapeRenderer
{
    public static void Draw(SKCanvas canvas, Shape shape)
    {
        using var path = ShapeGeometryFactory.Build(shape);
        var alpha = (byte)Math.Clamp(shape.Opacity / 100.0 * 255, 0, 255);
        var strokeOnly = ShapeGeometryFactory.IsStrokeOnly(shape.Kind);

        if (shape.ShadowEnabled)
        {
            DrawShadow(canvas, path, shape, strokeOnly);
        }

        if (!strokeOnly)
        {
            using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

            if (shape.FadeAmount > 0)
            {
                fillPaint.Shader = BuildFadeShader(shape, alpha);
            }
            else
            {
                fillPaint.Color = shape.FillColor.ToSkColor().WithAlpha(alpha);
            }

            if (shape.BlurRadius > 0)
            {
                fillPaint.ImageFilter = SKImageFilter.CreateBlur((float)shape.BlurRadius, (float)shape.BlurRadius);
            }

            canvas.DrawPath(path, fillPaint);
        }

        if (shape.BorderThickness > 0)
        {
            using var strokePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)shape.BorderThickness,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                Color = shape.BorderColor.ToSkColor().WithAlpha(alpha),
            };

            if (shape.BlurRadius > 0 && strokeOnly)
            {
                strokePaint.ImageFilter = SKImageFilter.CreateBlur((float)shape.BlurRadius, (float)shape.BlurRadius);
            }

            canvas.DrawPath(path, strokePaint);
        }
    }

    private static void DrawShadow(SKCanvas canvas, SKPath path, Shape shape, bool strokeOnly)
    {
        var blur = (float)(shape.ShadowStrength / 100.0 * 20);
        var shadowAlpha = (byte)Math.Clamp(shape.ShadowStrength / 100.0 * 180, 0, 255);

        using var shadowPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0, 0, 0, shadowAlpha),
            Style = strokeOnly ? SKPaintStyle.Stroke : SKPaintStyle.Fill,
            StrokeWidth = (float)Math.Max(shape.BorderThickness, 2),
            ImageFilter = SKImageFilter.CreateBlur(blur, blur),
        };

        canvas.Save();
        canvas.Translate((float)shape.ShadowDistance, (float)shape.ShadowDistance);
        canvas.DrawPath(path, shadowPaint);
        canvas.Restore();
    }

    private static SKShader BuildFadeShader(Shape shape, byte alpha)
    {
        var color = shape.FillColor.ToSkColor();
        var stop = Math.Clamp(1f - (float)(shape.FadeAmount / 100.0), 0f, 1f);

        return SKShader.CreateLinearGradient(
            new SKPoint((float)shape.X, (float)shape.Y),
            new SKPoint((float)shape.X, (float)(shape.Y + shape.Height)),
            [color.WithAlpha(alpha), color.WithAlpha(0)],
            [stop, 1f],
            SKShaderTileMode.Clamp);
    }
}
