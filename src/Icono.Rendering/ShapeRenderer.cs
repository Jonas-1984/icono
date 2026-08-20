using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

public static class ShapeRenderer
{
    public static void Draw(SKCanvas canvas, Shape shape)
    {
        var alpha = (byte)Math.Clamp(shape.Opacity / 100.0 * 255, 0, 255);

        if (shape.Kind == ShapeKind.Box3D)
        {
            using var outline = ShapeGeometryFactory.Build(shape);
            if (shape.ShadowEnabled)
            {
                DrawShadow(canvas, outline, shape, strokeOnly: false);
            }

            DrawBox3D(canvas, shape, alpha);
            return;
        }

        if (shape.Kind == ShapeKind.Text && !string.IsNullOrEmpty(shape.TextContent))
        {
            DrawTextShape(canvas, shape, alpha);
            return;
        }

        using var path = ShapeGeometryFactory.Build(shape);
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
            };

            if (shape.BorderStyleKind == BorderStyleKind.Gradient)
            {
                strokePaint.Shader = BuildBorderGradientShader(shape, alpha, fadeToTransparent: false);
            }
            else if (shape.BorderStyleKind == BorderStyleKind.Fade)
            {
                strokePaint.Shader = BuildBorderGradientShader(shape, alpha, fadeToTransparent: true);
            }
            else
            {
                strokePaint.Color = shape.BorderColor.ToSkColor().WithAlpha(alpha);
            }

            var effectiveBlur = shape.BorderBlurRadius > 0 ? shape.BorderBlurRadius : (strokeOnly ? shape.BlurRadius : 0);
            if (effectiveBlur > 0)
            {
                strokePaint.ImageFilter = SKImageFilter.CreateBlur((float)effectiveBlur, (float)effectiveBlur);
            }

            if (shape.Dashed)
            {
                strokePaint.PathEffect = SKPathEffect.CreateDash([6, 4], 0);
            }

            canvas.DrawPath(path, strokePaint);
        }
    }

    /// <summary>Draws a Box3D's three faces with a fixed lighten/darken shading ramp (top
    /// lightest, front the base color, side darkest) so a single FillColor still reads as a
    /// believable extruded box, then strokes all three face outlines in BorderColor.</summary>
    /// <summary>Draws the shape's actual typed text (see <see cref="Shape.TextContent"/>),
    /// centered within its X/Y/Width/Height box and rotated like any other shape — bypasses the
    /// generic fill/stroke/geometry pipeline entirely since text isn't a filled path.</summary>
    private static void DrawTextShape(SKCanvas canvas, Shape shape, byte alpha)
    {
        var text = shape.TextContent!;
        var weight = shape.TextBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = shape.TextItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        using var typeface = SKTypeface.FromFamilyName("Segoe UI", weight, SKFontStyleWidth.Normal, slant);
        using var font = new SKFont(typeface, (float)shape.TextFontSize);
        using var paint = new SKPaint { IsAntialias = true, Color = shape.FillColor.ToSkColor().WithAlpha(alpha) };

        var centerX = (float)(shape.X + shape.Width / 2);
        var centerY = (float)(shape.Y + shape.Height / 2);

        canvas.Save();
        if (shape.Rotation != 0)
        {
            canvas.RotateDegrees((float)shape.Rotation, centerX, centerY);
        }

        font.MeasureText(text, out var bounds, paint);
        canvas.DrawText(text, centerX - bounds.MidX, centerY - bounds.MidY, font, paint);
        canvas.Restore();
    }

    private static void DrawBox3D(SKCanvas canvas, Shape shape, byte alpha)
    {
        var (front, top, side) = ShapeGeometryFactory.BuildBox3DFaces(shape);
        using var frontPath = front;
        using var topPath = top;
        using var sidePath = side;

        var baseColor = shape.FillColor.ToSkColor().WithAlpha(alpha);

        using var facePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        facePaint.Color = Lighten(baseColor, 0.22f);
        canvas.DrawPath(topPath, facePaint);
        facePaint.Color = baseColor;
        canvas.DrawPath(frontPath, facePaint);
        facePaint.Color = Darken(baseColor, 0.22f);
        canvas.DrawPath(sidePath, facePaint);

        if (shape.BorderThickness > 0)
        {
            using var strokePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)shape.BorderThickness,
                StrokeJoin = SKStrokeJoin.Round,
                Color = shape.BorderColor.ToSkColor().WithAlpha(alpha),
            };

            canvas.DrawPath(frontPath, strokePaint);
            canvas.DrawPath(topPath, strokePaint);
            canvas.DrawPath(sidePath, strokePaint);
        }
    }

    private static SKColor Lighten(SKColor color, float amount) => new(
        (byte)(color.Red + (255 - color.Red) * amount),
        (byte)(color.Green + (255 - color.Green) * amount),
        (byte)(color.Blue + (255 - color.Blue) * amount),
        color.Alpha);

    private static SKColor Darken(SKColor color, float amount) => new(
        (byte)(color.Red * (1 - amount)),
        (byte)(color.Green * (1 - amount)),
        (byte)(color.Blue * (1 - amount)),
        color.Alpha);

    private static void DrawShadow(SKCanvas canvas, SKPath path, Shape shape, bool strokeOnly)
    {
        var blur = (float)(shape.ShadowStrength / 100.0 * 20);
        var shadowAlpha = (byte)Math.Clamp(shape.ShadowStrength / 100.0 * 180, 0, 255);

        using var shadowPaint = new SKPaint
        {
            IsAntialias = true,
            Color = shape.ShadowColor.ToSkColor().WithAlpha(shadowAlpha),
            Style = strokeOnly ? SKPaintStyle.Stroke : SKPaintStyle.Fill,
            StrokeWidth = (float)Math.Max(shape.BorderThickness, 2),
            ImageFilter = SKImageFilter.CreateBlur(blur, blur),
        };

        var angleRad = shape.ShadowAngle * Math.PI / 180.0;
        var dx = (float)(Math.Cos(angleRad) * shape.ShadowDistance);
        var dy = (float)(Math.Sin(angleRad) * shape.ShadowDistance);

        canvas.Save();
        canvas.Translate(dx, dy);
        canvas.DrawPath(path, shadowPaint);
        canvas.Restore();
    }

    /// <summary>Builds the border's Gradient/Fade shader. Gradient blends BorderColor into
    /// BorderGradientColor top-to-bottom; Fade blends BorderColor into transparent, mirroring
    /// the fill's <see cref="BuildFadeShader"/> semantics.</summary>
    private static SKShader BuildBorderGradientShader(Shape shape, byte alpha, bool fadeToTransparent)
    {
        var start = shape.BorderColor.ToSkColor().WithAlpha(alpha);
        var end = fadeToTransparent ? start.WithAlpha(0) : shape.BorderGradientColor.ToSkColor().WithAlpha(alpha);

        return SKShader.CreateLinearGradient(
            new SKPoint((float)shape.X, (float)shape.Y),
            new SKPoint((float)shape.X, (float)(shape.Y + shape.Height)),
            [start, end],
            [0f, 1f],
            SKShaderTileMode.Clamp);
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
