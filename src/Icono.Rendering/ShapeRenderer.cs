using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

public static class ShapeRenderer
{
    /// <summary>Combines a color's OWN stored alpha (its own transparency, as picked in
    /// <c>Icono.App.Views.ColorPickerView</c>) with the shape-level <paramref name="opacityAlpha"/>
    /// (from <see cref="Shape.Opacity"/> or a shadow's strength) by multiplying them, instead of
    /// the old behavior of just replacing the color's alpha with the shape's — which silently threw
    /// away any transparency the user picked for that specific fill/border/shadow color.</summary>
    private static SKColor WithCombinedAlpha(this ArgbColor color, byte opacityAlpha)
    {
        var sk = color.ToSkColor();
        return sk.WithAlpha((byte)(sk.Alpha * opacityAlpha / 255.0));
    }

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

        if (shape.Kind == ShapeKind.DesignGrid)
        {
            DesignGridRenderer.Draw(canvas, shape, alpha);
            return;
        }

        if (shape.Kind == ShapeKind.Text && !string.IsNullOrEmpty(shape.TextContent))
        {
            DrawTextShape(canvas, shape, alpha);
            return;
        }

        if (shape.Kind == ShapeKind.RasterImage && shape.ImageBytes is { Length: > 0 })
        {
            DrawRasterImageShape(canvas, shape, alpha);
            return;
        }

        using var path = ShapeGeometryFactory.Build(shape);
        var strokeOnly = shape.Kind == ShapeKind.CombinedPath
            ? shape.CustomPathStrokeOnly
            : ShapeGeometryFactory.IsStrokeOnly(shape.Kind);

        // A design style (Glassmorphism, Neumorphism, ...) replaces the plain fill + border look.
        if (shape.DesignStyle != DesignStyleKind.None && shape.FillEnabled && DesignStyleRenderer.Supports(shape))
        {
            if (shape.ShadowEnabled)
            {
                DrawShadow(canvas, path, shape, strokeOnly: false);
            }

            DesignStyleRenderer.Draw(canvas, path, shape, alpha);
            return;
        }

        if (shape.ShadowEnabled)
        {
            DrawShadow(canvas, path, shape, strokeOnly);
        }

        if (shape.FillEnabled)
        {
            // A "stroke-only" kind (Traffic Cone, StarCheck, ShieldCog, ...) still fills whatever
            // closed contour it has (Traffic Cone's cone + base plate, say) — it just draws that
            // fill IN ADDITION TO the stroke below, rather than never filling at all. A pure
            // open-line kind (Line, Arrow, ...) has no closed contour, so this ends up filling
            // nothing for those — no visible change from before. Either way, an open detail line
            // never gets auto-closed into a blob it was never meant to have; only the border
            // stroke draws those, using the full path unchanged.
            var fillClosedOnly = strokeOnly || ShapeGeometryFactory.HasMixedOpenAndClosedContours(shape.Kind);
            using var fillablePath = fillClosedOnly ? ShapeGeometryFactory.ExtractClosedContoursOnly(path) : null;
            var pathToFill = fillablePath ?? path;

            using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

            if (shape.FadeAmount > 0)
            {
                fillPaint.Shader = BuildFadeShader(shape, alpha);
            }
            else
            {
                fillPaint.Color = shape.FillColor.WithCombinedAlpha(alpha);
            }

            if (shape.BlurRadius > 0)
            {
                fillPaint.ImageFilter = SKImageFilter.CreateBlur((float)shape.BlurRadius, (float)shape.BlurRadius);
            }

            canvas.DrawPath(pathToFill, fillPaint);
        }

        if (shape.BorderThickness > 0)
        {
            using var strokePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)shape.BorderThickness,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = ShapeGeometryFactory.UsesSharpStrokeJoin(shape) ? SKStrokeJoin.Miter : SKStrokeJoin.Round,
                StrokeMiter = 10f,
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
                strokePaint.Color = shape.BorderColor.WithCombinedAlpha(alpha);
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
        var fontFamily = string.IsNullOrWhiteSpace(shape.TextFontFamily) ? "Segoe UI" : shape.TextFontFamily;
        using var typeface = SKTypeface.FromFamilyName(fontFamily, weight, SKFontStyleWidth.Normal, slant);
        using var font = new SKFont(typeface, (float)shape.TextFontSize);
        using var paint = new SKPaint { IsAntialias = true, Color = shape.FillColor.WithCombinedAlpha(alpha) };

        var centerX = (float)(shape.X + shape.Width / 2);
        var centerY = (float)(shape.Y + shape.Height / 2);

        canvas.Save();
        if (shape.Rotation != 0)
        {
            canvas.RotateDegrees((float)shape.Rotation, centerX, centerY);
        }

        font.MeasureText(text, out var bounds, paint);
        var textX = centerX - bounds.MidX;
        var textY = centerY - bounds.MidY;
        canvas.DrawText(text, textX, textY, font, paint);

        if (shape.TextUnderline)
        {
            using var underlinePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)Math.Max(1, shape.TextFontSize / 16),
                Color = shape.FillColor.WithCombinedAlpha(alpha),
            };

            var underlineY = textY + (float)shape.TextFontSize * 0.12f;
            canvas.DrawLine(textX + bounds.Left, underlineY, textX + bounds.Right, underlineY, underlinePaint);
        }

        canvas.Restore();
    }

    /// <summary>Draws an imported raster image (see <see cref="Shape.ImageBytes"/>), stretched to
    /// fill X/Y/Width/Height and rotated like any other shape — bypasses the generic fill/stroke
    /// pipeline entirely since a bitmap isn't a filled path (same approach as <see cref="DrawTextShape"/>).</summary>
    /// <summary>Draws an imported raster image with the same adjustment set every other shape
    /// gets — border, shadow, blur, corner-radius (as a clip mask), rotation, and flip — instead of
    /// just a plain stretched bitmap. Only <see cref="Shape.FadeAmount"/> has no effect here (a
    /// top-to-bottom alpha gradient over a bitmap needs blend-mode compositing, not yet wired up).</summary>
    private static void DrawRasterImageShape(SKCanvas canvas, Shape shape, byte alpha)
    {
        using var bitmap = SKBitmap.Decode(shape.ImageBytes);
        if (bitmap is null)
        {
            return;
        }

        // Same outline ShapeGeometryFactory hands every other kind — already flip/rotate/corner-
        // radius baked in — so the border stroke and the corner-radius clip both match exactly
        // what a vector shape with this Width/Height/Rotation/CornerRadius would show.
        using var outline = ShapeGeometryFactory.Build(shape);

        if (shape.ShadowEnabled)
        {
            DrawShadow(canvas, outline, shape, strokeOnly: false);
        }

        var destRect = SKRect.Create((float)shape.X, (float)shape.Y, (float)shape.Width, (float)shape.Height);
        var centerX = (float)(shape.X + shape.Width / 2);
        var centerY = (float)(shape.Y + shape.Height / 2);

        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(alpha) };
        if (shape.BlurRadius > 0)
        {
            paint.ImageFilter = SKImageFilter.CreateBlur((float)shape.BlurRadius, (float)shape.BlurRadius);
        }

        canvas.Save();

        if (shape.CornerRadius > 0)
        {
            canvas.ClipPath(outline, antialias: true);
        }

        if (shape.Rotation != 0)
        {
            canvas.RotateDegrees((float)shape.Rotation, centerX, centerY);
        }

        if (shape.FlipHorizontal || shape.FlipVertical)
        {
            canvas.Scale(shape.FlipHorizontal ? -1 : 1, shape.FlipVertical ? -1 : 1, centerX, centerY);
        }

        canvas.DrawBitmap(bitmap, destRect, paint);
        canvas.Restore();

        if (shape.BorderThickness > 0)
        {
            using var strokePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)shape.BorderThickness,
                StrokeJoin = SKStrokeJoin.Round,
                Color = shape.BorderColor.WithCombinedAlpha(alpha),
            };

            canvas.DrawPath(outline, strokePaint);
        }
    }

    private static void DrawBox3D(SKCanvas canvas, Shape shape, byte alpha)
    {
        var (front, top, side) = ShapeGeometryFactory.BuildBox3DFaces(shape);
        using var frontPath = front;
        using var topPath = top;
        using var sidePath = side;

        if (shape.FillEnabled)
        {
            var baseColor = shape.FillColor.WithCombinedAlpha(alpha);

            using var facePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            facePaint.Color = Lighten(baseColor, 0.22f);
            canvas.DrawPath(topPath, facePaint);
            facePaint.Color = baseColor;
            canvas.DrawPath(frontPath, facePaint);
            facePaint.Color = Darken(baseColor, 0.22f);
            canvas.DrawPath(sidePath, facePaint);
        }

        if (shape.BorderThickness > 0)
        {
            using var strokePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)shape.BorderThickness,
                StrokeJoin = SKStrokeJoin.Round,
                Color = shape.BorderColor.WithCombinedAlpha(alpha),
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
        var blur = (float)shape.ShadowBlurRadius;
        var shadowAlpha = (byte)Math.Clamp(shape.ShadowStrength / 100.0 * 255, 0, 255);

        using var shadowPaint = new SKPaint
        {
            IsAntialias = true,
            Color = shape.ShadowColor.WithCombinedAlpha(shadowAlpha),
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
        var start = shape.BorderColor.WithCombinedAlpha(alpha);
        var end = fadeToTransparent ? start.WithAlpha(0) : shape.BorderGradientColor.WithCombinedAlpha(alpha);

        return SKShader.CreateLinearGradient(
            new SKPoint((float)shape.X, (float)shape.Y),
            new SKPoint((float)shape.X, (float)(shape.Y + shape.Height)),
            [start, end],
            [0f, 1f],
            SKShaderTileMode.Clamp);
    }

    /// <summary>Builds the fill fade's alpha gradient — direction and single-vs-both-sides
    /// controlled by <see cref="Shape.FadeDirection"/>. The two "BothSides" modes split
    /// <see cref="Shape.FadeAmount"/> evenly between both ends and stay solid in the middle,
    /// instead of the single-direction modes' solid-one-edge-to-transparent-other-edge ramp.</summary>
    private static SKShader BuildFadeShader(Shape shape, byte alpha)
    {
        var color = shape.FillColor.WithCombinedAlpha(alpha);
        var transparent = color.WithAlpha(0);
        var fadeFraction = Math.Clamp((float)(shape.FadeAmount / 100.0), 0f, 1f);

        var left = (float)shape.X;
        var top = (float)shape.Y;
        var right = (float)(shape.X + shape.Width);
        var bottom = (float)(shape.Y + shape.Height);

        switch (shape.FadeDirection)
        {
            case FadeDirection.BottomToTop:
                return SKShader.CreateLinearGradient(
                    new SKPoint(left, bottom), new SKPoint(left, top),
                    [color, transparent], [1f - fadeFraction, 1f], SKShaderTileMode.Clamp);
            case FadeDirection.LeftToRight:
                return SKShader.CreateLinearGradient(
                    new SKPoint(left, top), new SKPoint(right, top),
                    [color, transparent], [1f - fadeFraction, 1f], SKShaderTileMode.Clamp);
            case FadeDirection.RightToLeft:
                return SKShader.CreateLinearGradient(
                    new SKPoint(right, top), new SKPoint(left, top),
                    [color, transparent], [1f - fadeFraction, 1f], SKShaderTileMode.Clamp);
            case FadeDirection.VerticalBothSides:
            {
                var half = Math.Clamp(fadeFraction / 2f, 0f, 0.5f);
                return SKShader.CreateLinearGradient(
                    new SKPoint(left, top), new SKPoint(left, bottom),
                    [transparent, color, color, transparent], [0f, half, 1f - half, 1f], SKShaderTileMode.Clamp);
            }
            case FadeDirection.HorizontalBothSides:
            {
                var half = Math.Clamp(fadeFraction / 2f, 0f, 0.5f);
                return SKShader.CreateLinearGradient(
                    new SKPoint(left, top), new SKPoint(right, top),
                    [transparent, color, color, transparent], [0f, half, 1f - half, 1f], SKShaderTileMode.Clamp);
            }
            default:
                return SKShader.CreateLinearGradient(
                    new SKPoint(left, top), new SKPoint(left, bottom),
                    [color, transparent], [1f - fadeFraction, 1f], SKShaderTileMode.Clamp);
        }
    }
}
