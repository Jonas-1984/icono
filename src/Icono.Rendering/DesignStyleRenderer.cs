using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>
/// Draws a closed shape in one of the UI "design languages" of <see cref="DesignStyleKind"/>. Every style
/// takes the shape's own <see cref="Shape.FillColor"/> as its base colour and builds the look from layered
/// gradients, drop/inner shadows, glows and hairline borders — so changing the colour re-tints the whole style.
/// The geometry (already rotated/flipped/rounded) is handed in; Opacity and Blur of the shape are applied
/// to the finished result as one layer.
/// </summary>
public static class DesignStyleRenderer
{
    /// <summary>Styles apply to closed, fillable shapes only: not to the open line family, text, bitmaps or Box3D.</summary>
    public static bool Supports(Shape shape) =>
        shape.Kind is not (ShapeKind.Text or ShapeKind.RasterImage or ShapeKind.Box3D or ShapeKind.DesignGrid)
        && !(shape.Kind == ShapeKind.CombinedPath ? shape.CustomPathStrokeOnly : ShapeGeometryFactory.IsStrokeOnly(shape.Kind));

    internal static void Draw(SKCanvas canvas, SKPath path, Shape shape, byte alpha)
    {
        var bounds = path.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var baseColor = shape.FillColor.ToSkColor();
        var layerAlpha = (byte)(alpha * baseColor.Alpha / 255.0);
        var surface = baseColor.WithAlpha(255);
        var m = Math.Min(bounds.Width, bounds.Height);

        using var layerPaint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(layerAlpha) };
        if (shape.BlurRadius > 0)
        {
            layerPaint.ImageFilter = SKImageFilter.CreateBlur((float)shape.BlurRadius, (float)shape.BlurRadius);
        }

        canvas.SaveLayer(layerPaint);

        switch (shape.DesignStyle)
        {
            case DesignStyleKind.Glassmorphism: DrawGlass(canvas, path, bounds, surface, m); break;
            case DesignStyleKind.Neumorphism: DrawNeumorphism(canvas, path, bounds, surface, m); break;
            case DesignStyleKind.FluentDesign: DrawFluent(canvas, path, bounds, surface, m); break;
            case DesignStyleKind.AuroraGradients: DrawAurora(canvas, path, bounds, surface, m); break;
            case DesignStyleKind.BentoUI: DrawBento(canvas, path, bounds, surface, m, shape.CornerRadius); break;
            case DesignStyleKind.Cyberpunk: DrawCyberpunk(canvas, path, bounds, surface, m); break;
            case DesignStyleKind.Claymorphism3D: DrawClay(canvas, path, bounds, surface, m); break;
            case DesignStyleKind.DarkNeon: DrawDarkNeon(canvas, path, bounds, surface, m); break;
        }

        canvas.Restore();
    }

    // ---------------------------------------------------------------------------------------------
    // Glassmorphism — frosted translucent pane: soft tinted shadow, milky gradient body, light bloom
    // in the top-left corner and a bright glass-edge border that fades toward the bottom-right.
    // ---------------------------------------------------------------------------------------------
    private static void DrawGlass(SKCanvas c, SKPath p, SKRect b, SKColor surface, float m)
    {
        DropShadowOnly(c, p, 0, m * 0.07f, m * 0.08f, Darken(surface, 0.35f).WithAlpha(95));

        using (var body = new SKPaint
        {
            IsAntialias = true,
            Shader = LinearGradient(b, [Mix(surface, SKColors.White, 0.55f).WithAlpha(165), Mix(surface, SKColors.White, 0.2f).WithAlpha(70)]),
        })
        {
            c.DrawPath(p, body);
        }

        c.Save();
        c.ClipPath(p, antialias: true);
        using (var bloom = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(b.Left + b.Width * 0.2f, b.Top + b.Height * 0.12f), m * 0.95f,
                [SKColors.White.WithAlpha(105), SKColors.White.WithAlpha(0)], [0f, 1f], SKShaderTileMode.Clamp),
        })
        {
            c.DrawRect(b, bloom);
        }

        c.Restore();

        using var edge = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.2f, m * 0.014f),
            Shader = LinearGradient(b, [SKColors.White.WithAlpha(235), SKColors.White.WithAlpha(45), SKColors.White.WithAlpha(130)], [0f, 0.55f, 1f]),
        };
        c.DrawPath(p, edge);
    }

    // ---------------------------------------------------------------------------------------------
    // Neumorphism — soft extrusion out of the surface: a light shadow from the top-left, a dark one to
    // the bottom-right, and a barely-there light-to-dark gradient on the face. No border.
    // ---------------------------------------------------------------------------------------------
    private static void DrawNeumorphism(SKCanvas c, SKPath p, SKRect b, SKColor surface, float m)
    {
        var dist = Math.Clamp(m * 0.07f, 3f, 18f);
        var sigma = dist * 1.2f;

        DropShadowOnly(c, p, -dist, -dist, sigma, Mix(surface, SKColors.White, 0.6f).WithAlpha(235));
        DropShadowOnly(c, p, dist, dist, sigma, Mix(surface, SKColors.Black, 0.5f).WithAlpha(210));

        using var face = new SKPaint
        {
            IsAntialias = true,
            Shader = LinearGradient(b, [Mix(surface, SKColors.White, 0.08f), Mix(surface, SKColors.Black, 0.07f)]),
        };
        c.DrawPath(p, face);
    }

    // ---------------------------------------------------------------------------------------------
    // Fluent Design — acrylic surface with layered elevation shadow, a top "light" gradient, a reveal
    // highlight and a 1 px lighter-top / darker-bottom edge.
    // ---------------------------------------------------------------------------------------------
    private static void DrawFluent(SKCanvas c, SKPath p, SKRect b, SKColor surface, float m)
    {
        DropShadowOnly(c, p, 0, Math.Clamp(m * 0.05f, 2f, 9f), Math.Clamp(m * 0.06f, 3f, 12f), SKColors.Black.WithAlpha(80));
        DropShadowOnly(c, p, 0, 1f, 1.5f, SKColors.Black.WithAlpha(60));

        using (var acrylic = new SKPaint { IsAntialias = true, Color = surface.WithAlpha(238) })
        {
            c.DrawPath(p, acrylic);
        }

        c.Save();
        c.ClipPath(p, antialias: true);
        using (var sheen = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, b.Top), new SKPoint(0, b.Bottom),
                [SKColors.White.WithAlpha(46), SKColors.White.WithAlpha(6), SKColors.Black.WithAlpha(22)], [0f, 0.5f, 1f], SKShaderTileMode.Clamp),
        })
        {
            c.DrawRect(b, sheen);
        }

        using (var reveal = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(b.Left + b.Width * 0.18f, b.Top + b.Height * 0.2f), m * 0.6f,
                [SKColors.White.WithAlpha(52), SKColors.White.WithAlpha(0)], [0f, 1f], SKShaderTileMode.Clamp),
        })
        {
            c.DrawRect(b, reveal);
        }

        c.Restore();

        using var edge = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, b.Top), new SKPoint(0, b.Bottom),
                [SKColors.White.WithAlpha(110), SKColors.White.WithAlpha(28), SKColors.Black.WithAlpha(90)], [0f, 0.5f, 1f], SKShaderTileMode.Clamp),
        };
        c.DrawPath(p, edge);
    }

    // ---------------------------------------------------------------------------------------------
    // Aurora Gradients — a flowing multi-hue gradient derived from the base colour (hue shifts across
    // the shape) with two soft light blobs and a faint coloured glow underneath.
    // ---------------------------------------------------------------------------------------------
    private static void DrawAurora(SKCanvas c, SKPath p, SKRect b, SKColor surface, float m)
    {
        surface.ToHsv(out var h, out var s, out var v);
        var sat = Math.Max(s, 62f);
        var val = Math.Max(v, 92f);
        var c1 = SKColor.FromHsv(h, sat, val);
        var c2 = SKColor.FromHsv((h + 65) % 360, sat, val);
        var c3 = SKColor.FromHsv((h + 140) % 360, sat, val);
        var c4 = SKColor.FromHsv((h + 230) % 360, sat * 0.9f, val);

        DropShadowOnly(c, p, 0, m * 0.07f, m * 0.1f, c2.WithAlpha(110));

        using (var body = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(b.Left, b.Bottom), new SKPoint(b.Right, b.Top), [c1, c2, c3], [0f, 0.55f, 1f], SKShaderTileMode.Clamp),
        })
        {
            c.DrawPath(p, body);
        }

        c.Save();
        c.ClipPath(p, antialias: true);
        DrawBlob(c, b, new SKPoint(b.Left + b.Width * 0.15f, b.Top + b.Height * 0.85f), m * 0.85f, c4.WithAlpha(215));
        DrawBlob(c, b, new SKPoint(b.Left + b.Width * 0.85f, b.Top + b.Height * 0.15f), m * 0.8f, Mix(c2, SKColors.White, 0.45f).WithAlpha(190));
        DrawBlob(c, b, new SKPoint(b.Left + b.Width * 0.5f, b.Top + b.Height * 0.55f), m * 0.55f, Mix(c3, SKColors.White, 0.3f).WithAlpha(110));
        c.Restore();

        using var edge = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1f, m * 0.01f),
            Color = SKColors.White.WithAlpha(85),
        };
        c.DrawPath(p, edge);
    }

    private static void DrawBlob(SKCanvas c, SKRect area, SKPoint centre, float radius, SKColor color)
    {
        using var blob = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(centre, radius, [color, color.WithAlpha(0)], [0f, 1f], SKShaderTileMode.Clamp),
        };
        c.DrawRect(area, blob);
    }

    // ---------------------------------------------------------------------------------------------
    // Bento UI — a calm rounded tile: large radius, flat surface with a whisper of top light, hairline
    // border and two very soft shadows.
    // ---------------------------------------------------------------------------------------------
    private static void DrawBento(SKCanvas c, SKPath p, SKRect b, SKColor surface, float m, double userRadius)
    {
        var radius = Math.Max(m * 0.2f, (float)userRadius);
        using var rounding = SKPathEffect.CreateCorner(radius);

        DropShadowOnly(c, p, 0, Math.Clamp(m * 0.1f, 4f, 16f), Math.Clamp(m * 0.1f, 5f, 18f), SKColors.Black.WithAlpha(46), rounding);
        DropShadowOnly(c, p, 0, 1.5f, 2f, SKColors.Black.WithAlpha(52), rounding);

        using (var face = new SKPaint
        {
            IsAntialias = true,
            PathEffect = rounding,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, b.Top), new SKPoint(0, b.Bottom),
                [Mix(surface, SKColors.White, 0.1f), surface], [0f, 1f], SKShaderTileMode.Clamp),
        })
        {
            c.DrawPath(p, face);
        }

        using var edge = new SKPaint
        {
            IsAntialias = true,
            PathEffect = rounding,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, b.Top), new SKPoint(0, b.Bottom),
                [SKColors.White.WithAlpha(120), Mix(surface, SKColors.Black, 0.22f).WithAlpha(150)], [0f, 1f], SKShaderTileMode.Clamp),
        };
        c.DrawPath(p, edge);
    }

    // ---------------------------------------------------------------------------------------------
    // Cyberpunk / Futuristic — near-black violet panel, CRT scanlines, a cyan neon outline with glow and
    // a magenta "misregistered" offset outline.
    // ---------------------------------------------------------------------------------------------
    private static void DrawCyberpunk(SKCanvas c, SKPath p, SKRect b, SKColor surface, float m)
    {
        var cyan = new SKColor(0x00, 0xE5, 0xFF);
        var magenta = new SKColor(0xFF, 0x2B, 0xD6);
        var w = Math.Max(1.6f, m * 0.025f);

        using (var panel = new SKPaint
        {
            IsAntialias = true,
            Shader = LinearGradient(b, [Mix(new SKColor(0x08, 0x0A, 0x1E), surface, 0.12f), Mix(new SKColor(0x1E, 0x0B, 0x36), surface, 0.12f)]),
        })
        {
            c.DrawPath(p, panel);
        }

        c.Save();
        c.ClipPath(p, antialias: true);
        using (var lines = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = cyan.WithAlpha(34) })
        {
            var step = Math.Max(3f, m * 0.035f);
            for (var y = b.Top + step; y < b.Bottom; y += step)
            {
                c.DrawLine(b.Left, y, b.Right, y, lines);
            }
        }

        using (var glowInner = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(b.MidX, b.Bottom), m * 0.9f, [magenta.WithAlpha(70), magenta.WithAlpha(0)], [0f, 1f], SKShaderTileMode.Clamp),
        })
        {
            c.DrawRect(b, glowInner);
        }

        c.Restore();

        // misregistered magenta outline
        c.Save();
        c.Translate(w * 1.4f, w * 1.4f);
        using (var ghost = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = w, Color = magenta.WithAlpha(190) })
        {
            c.DrawPath(p, ghost);
        }

        c.Restore();

        Glow(c, p, cyan, w * 2.4f, w * 1.6f, 200);

        using var neon = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = w, Color = Mix(cyan, SKColors.White, 0.35f) };
        c.DrawPath(p, neon);
    }

    // ---------------------------------------------------------------------------------------------
    // 3D Claymorphism — an inflated, puffy object: coloured drop shadow, radial light from the top-left,
    // inner shadow along the bottom-right edge, inner highlight along the top-left edge and a soft
    // specular blob.
    // ---------------------------------------------------------------------------------------------
    private static void DrawClay(SKCanvas c, SKPath p, SKRect b, SKColor surface, float m)
    {
        DropShadowOnly(c, p, m * 0.05f, m * 0.1f, m * 0.1f, Darken(surface, 0.45f).WithAlpha(150));

        c.Save();
        c.ClipPath(p, antialias: true);

        using (var body = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(b.Left + b.Width * 0.3f, b.Top + b.Height * 0.25f), Math.Max(b.Width, b.Height) * 0.9f,
                [Mix(surface, SKColors.White, 0.32f), surface, Darken(surface, 0.2f)], [0f, 0.55f, 1f], SKShaderTileMode.Clamp),
        })
        {
            c.DrawRect(b, body);
        }

        var d = m * 0.05f;

        // inner shadow (bottom-right): the outline shifted up-left, stroked thick and blurred
        c.Save();
        c.Translate(-d, -d);
        using (var innerShadow = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = m * 0.22f,
            Color = Darken(surface, 0.5f).WithAlpha(150),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, m * 0.07f),
        })
        {
            c.DrawPath(p, innerShadow);
        }

        c.Restore();

        // inner highlight (top-left): the outline shifted down-right
        c.Save();
        c.Translate(d, d);
        using (var innerLight = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = m * 0.18f,
            Color = SKColors.White.WithAlpha(175),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, m * 0.06f),
        })
        {
            c.DrawPath(p, innerLight);
        }

        c.Restore();

        // specular blob
        using (var spec = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White.WithAlpha(95),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, m * 0.05f),
        })
        {
            c.DrawOval(new SKRect(
                b.Left + b.Width * 0.16f, b.Top + b.Height * 0.12f,
                b.Left + b.Width * 0.46f, b.Top + b.Height * 0.26f), spec);
        }

        c.Restore();
    }

    // ---------------------------------------------------------------------------------------------
    // Dark UI + Neon Accents — a dark, barely tinted card with a single saturated neon edge that glows
    // outward and a faint inner glow.
    // ---------------------------------------------------------------------------------------------
    private static void DrawDarkNeon(SKCanvas c, SKPath p, SKRect b, SKColor surface, float m)
    {
        surface.ToHsv(out var h, out var s, out _);
        var neon = SKColor.FromHsv(h, Math.Max(s, 82f), 100f);
        var w = Math.Max(1.5f, m * 0.022f);

        DropShadowOnly(c, p, 0, 0, m * 0.07f, neon.WithAlpha(120));

        using (var card = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, b.Top), new SKPoint(0, b.Bottom),
                [Mix(new SKColor(0x1C, 0x1D, 0x26), surface, 0.1f), Mix(new SKColor(0x0A, 0x0B, 0x10), surface, 0.08f)],
                [0f, 1f], SKShaderTileMode.Clamp),
        })
        {
            c.DrawPath(p, card);
        }

        c.Save();
        c.ClipPath(p, antialias: true);
        using (var inner = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = m * 0.16f,
            Color = neon.WithAlpha(105),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, m * 0.07f),
        })
        {
            c.DrawPath(p, inner);
        }

        c.Restore();

        Glow(c, p, neon, w * 2.6f, w * 1.8f, 210);

        using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = w, Color = Mix(neon, SKColors.White, 0.25f) };
        c.DrawPath(p, edge);
    }

    // ---------------------------------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>Draws only the (offset, blurred) shadow of the path — never the path itself.</summary>
    private static void DropShadowOnly(SKCanvas c, SKPath p, float dx, float dy, float sigma, SKColor color, SKPathEffect? effect = null)
    {
        using var filter = SKImageFilter.CreateDropShadowOnly(dx, dy, sigma, sigma, color);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black, ImageFilter = filter, PathEffect = effect };
        c.DrawPath(p, paint);
    }

    /// <summary>A blurred stroke around the outline, drawn under the crisp neon edge.</summary>
    private static void Glow(SKCanvas c, SKPath p, SKColor color, float width, float sigma, byte alpha)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            Color = color.WithAlpha(alpha),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma),
        };
        c.DrawPath(p, paint);
    }

    private static SKShader LinearGradient(SKRect b, SKColor[] colors, float[]? positions = null) =>
        SKShader.CreateLinearGradient(new SKPoint(b.Left, b.Top), new SKPoint(b.Right, b.Bottom), colors, positions, SKShaderTileMode.Clamp);

    private static SKColor Mix(SKColor a, SKColor b, float t) => new(
        (byte)(a.Red + (b.Red - a.Red) * t),
        (byte)(a.Green + (b.Green - a.Green) * t),
        (byte)(a.Blue + (b.Blue - a.Blue) * t),
        a.Alpha);

    private static SKColor Darken(SKColor c, float t) => Mix(c, SKColors.Black, t);
}
