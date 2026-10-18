using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>
/// Draws a closed shape in one of the UI "design languages" of <see cref="DesignStyleKind"/>. Every style
/// takes the shape's own <see cref="Shape.FillColor"/> as its base colour and builds the look from layered
/// gradients, inner/drop shadows, glows and an edge line.
///
/// The styles stay fully editable through the Properties panel: the edge line IS the shape's border
/// (thickness, colour, Solid/Gradient/Fade, dashed, blur — for the neon styles the border blur is the glow
/// radius), the corner radius shapes the outline, and the Effects card applies on top (opacity, blur, fade, and
/// a user drop shadow that takes over from the style's built-in one). <see cref="DesignStylePresets"/> fills in
/// sensible border/shadow values whenever a style is chosen.
/// </summary>
public static class DesignStyleRenderer
{
    /// <summary>Styles apply to closed, fillable shapes (text included, via its outline) — not to the open line family, bitmaps, Box3D or a design grid.</summary>
    public static bool Supports(Shape shape) =>
        shape.Kind is not (ShapeKind.RasterImage or ShapeKind.Box3D or ShapeKind.DesignGrid)
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

        // The style's own drop shadow gives way as soon as the user switches on the Effects card's shadow.
        var builtInShadow = !shape.ShadowEnabled;

        using var layerPaint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(layerAlpha) };
        if (shape.BlurRadius > 0)
        {
            layerPaint.ImageFilter = SKImageFilter.CreateBlur((float)shape.BlurRadius, (float)shape.BlurRadius);
        }

        canvas.SaveLayer(layerPaint);

        switch (shape.DesignStyle)
        {
            case DesignStyleKind.Glassmorphism: DrawGlass(canvas, path, bounds, surface, m, shape, builtInShadow); break;
            case DesignStyleKind.Neumorphism: DrawNeumorphism(canvas, path, bounds, surface, m, shape, builtInShadow); break;
            case DesignStyleKind.FluentDesign: DrawFluent(canvas, path, bounds, surface, m, shape, builtInShadow); break;
            case DesignStyleKind.AuroraGradients: DrawAurora(canvas, path, bounds, surface, m, shape, builtInShadow); break;
            case DesignStyleKind.BentoUI: DrawBento(canvas, path, bounds, surface, m, shape, builtInShadow); break;
            case DesignStyleKind.Cyberpunk: DrawCyberpunk(canvas, path, bounds, surface, m, shape); break;
            case DesignStyleKind.Claymorphism3D: DrawClay(canvas, path, bounds, surface, m, builtInShadow); break;
            case DesignStyleKind.DarkNeon: DrawDarkNeon(canvas, path, bounds, surface, m, shape, builtInShadow); break;
        }

        // Effects card: fade the whole styled result out along the chosen direction.
        if (shape.FadeAmount > 0)
        {
            using var mask = new SKPaint { BlendMode = SKBlendMode.DstIn, Shader = ShapeRenderer.BuildFadeShader(shape, SKColors.White) };
            canvas.DrawPaint(mask);
        }

        canvas.Restore();
    }

    // ---------------------------------------------------------------------------------------------
    // Glassmorphism — frosted translucent pane: soft tinted shadow, milky gradient body, light bloom in
    // the top-left corner and a glass-edge border.
    // ---------------------------------------------------------------------------------------------
    private static void DrawGlass(SKCanvas c, SKPath p, SKRect b, SKColor surface, float m, Shape shape, bool shadow)
    {
        if (shadow)
        {
            DropShadowOnly(c, p, 0, m * 0.07f, m * 0.08f, Darken(surface, 0.35f).WithAlpha(95));
        }

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

        DrawEdge(c, p, shape, glow: false);
    }

    // ---------------------------------------------------------------------------------------------
    // Neumorphism — soft extrusion out of the surface: a light shadow from the top-left, a dark one to
    // the bottom-right, and a barely-there light-to-dark gradient on the face.
    // ---------------------------------------------------------------------------------------------
    private static void DrawNeumorphism(SKCanvas c, SKPath p, SKRect b, SKColor surface, float m, Shape shape, bool shadow)
    {
        var dist = Math.Clamp(m * 0.07f, 3f, 18f);
        var sigma = dist * 1.2f;

        DropShadowOnly(c, p, -dist, -dist, sigma, Mix(surface, SKColors.White, 0.6f).WithAlpha(235));
        if (shadow)
        {
            DropShadowOnly(c, p, dist, dist, sigma, Mix(surface, SKColors.Black, 0.5f).WithAlpha(210));
        }

        using var face = new SKPaint
        {
            IsAntialias = true,
            Shader = LinearGradient(b, [Mix(surface, SKColors.White, 0.08f), Mix(surface, SKColors.Black, 0.07f)]),
        };
        c.DrawPath(p, face);

        DrawEdge(c, p, shape, glow: false);
    }

    // ---------------------------------------------------------------------------------------------
    // Fluent Design — acrylic surface with layered elevation shadow, a top "light" gradient, a reveal
    // highlight and a crisp edge.
    // ---------------------------------------------------------------------------------------------
    private static void DrawFluent(SKCanvas c, SKPath p, SKRect b, SKColor surface, float m, Shape shape, bool shadow)
    {
        if (shadow)
        {
            DropShadowOnly(c, p, 0, Math.Clamp(m * 0.05f, 2f, 9f), Math.Clamp(m * 0.06f, 3f, 12f), SKColors.Black.WithAlpha(80));
            DropShadowOnly(c, p, 0, 1f, 1.5f, SKColors.Black.WithAlpha(60));
        }

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

        DrawEdge(c, p, shape, glow: false);
    }

    // ---------------------------------------------------------------------------------------------
    // Aurora Gradients — a flowing multi-hue gradient derived from the base colour (hue shifts across
    // the shape) with two soft light blobs and a faint coloured glow underneath.
    // ---------------------------------------------------------------------------------------------
    private static void DrawAurora(SKCanvas c, SKPath p, SKRect b, SKColor surface, float m, Shape shape, bool shadow)
    {
        surface.ToHsv(out var h, out var s, out var v);
        var sat = Math.Max(s, 62f);
        var val = Math.Max(v, 92f);
        var c1 = SKColor.FromHsv(h, sat, val);
        var c2 = SKColor.FromHsv((h + 65) % 360, sat, val);
        var c3 = SKColor.FromHsv((h + 140) % 360, sat, val);
        var c4 = SKColor.FromHsv((h + 230) % 360, sat * 0.9f, val);

        if (shadow)
        {
            DropShadowOnly(c, p, 0, m * 0.07f, m * 0.1f, c2.WithAlpha(110));
        }

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

        DrawEdge(c, p, shape, glow: false);
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
    // Bento UI — a calm tile: flat surface with a whisper of top light, an edge line and two very soft
    // shadows. Its generous rounding is the shape's own corner radius (set by the preset).
    // ---------------------------------------------------------------------------------------------
    private static void DrawBento(SKCanvas c, SKPath p, SKRect b, SKColor surface, float m, Shape shape, bool shadow)
    {
        if (shadow)
        {
            DropShadowOnly(c, p, 0, Math.Clamp(m * 0.1f, 4f, 16f), Math.Clamp(m * 0.1f, 5f, 18f), SKColors.Black.WithAlpha(46));
            DropShadowOnly(c, p, 0, 1.5f, 2f, SKColors.Black.WithAlpha(52));
        }

        using (var face = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, b.Top), new SKPoint(0, b.Bottom),
                [Mix(surface, SKColors.White, 0.1f), surface], [0f, 1f], SKShaderTileMode.Clamp),
        })
        {
            c.DrawPath(p, face);
        }

        DrawEdge(c, p, shape, glow: false);
    }

    // ---------------------------------------------------------------------------------------------
    // Cyberpunk / Futuristic — near-black violet panel, CRT scanlines, a neon outline (the border, with its
    // blur as glow) and a magenta "misregistered" offset outline.
    // ---------------------------------------------------------------------------------------------
    private static void DrawCyberpunk(SKCanvas c, SKPath p, SKRect b, SKColor surface, float m, Shape shape)
    {
        var cyan = new SKColor(0x00, 0xE5, 0xFF);
        var magenta = new SKColor(0xFF, 0x2B, 0xD6);
        var w = Math.Max(1f, (float)shape.BorderThickness);

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

        if (shape.BorderThickness > 0)
        {
            // misregistered magenta outline
            c.Save();
            c.Translate(w * 1.4f, w * 1.4f);
            using (var ghost = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = w, Color = magenta.WithAlpha(190) })
            {
                c.DrawPath(p, ghost);
            }

            c.Restore();
        }

        DrawEdge(c, p, shape, glow: true);
    }

    // ---------------------------------------------------------------------------------------------
    // 3D Claymorphism — an inflated, puffy object: coloured drop shadow, radial light from the top-left,
    // inner shadow along the bottom-right edge, inner highlight along the top-left edge and a soft
    // specular blob.
    // ---------------------------------------------------------------------------------------------
    private static void DrawClay(SKCanvas c, SKPath p, SKRect b, SKColor surface, float m, bool shadow)
    {
        if (shadow)
        {
            DropShadowOnly(c, p, m * 0.05f, m * 0.1f, m * 0.1f, Darken(surface, 0.45f).WithAlpha(150));
        }

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
    // Dark UI + Neon Accents — a dark, barely tinted card with a single saturated neon edge (the border)
    // that glows outward (its blur = glow radius) and a faint inner glow in the border colour.
    // ---------------------------------------------------------------------------------------------
    private static void DrawDarkNeon(SKCanvas c, SKPath p, SKRect b, SKColor surface, float m, Shape shape, bool shadow)
    {
        var neon = shape.BorderColor.ToSkColor().WithAlpha(255);

        if (shadow)
        {
            DropShadowOnly(c, p, 0, 0, m * 0.07f, neon.WithAlpha(110));
        }

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

        if (shape.BorderThickness > 0)
        {
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
        }

        DrawEdge(c, p, shape, glow: true);
    }

    // ---------------------------------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>The style's edge line, drawn from the shape's Border settings: thickness (0 = none), colour, Solid /
    /// Gradient / Fade, dashed and blur. With <paramref name="glow"/> (the neon styles) the border blur does not soften the
    /// line itself but becomes the radius of a halo drawn underneath the crisp line.</summary>
    private static void DrawEdge(SKCanvas c, SKPath p, Shape shape, bool glow)
    {
        var t = (float)shape.BorderThickness;
        if (t <= 0)
        {
            return;
        }

        SKShader? shader = shape.BorderStyleKind switch
        {
            BorderStyleKind.Gradient => ShapeRenderer.BuildBorderGradientShader(shape, 255, fadeToTransparent: false),
            BorderStyleKind.Fade => ShapeRenderer.BuildBorderGradientShader(shape, 255, fadeToTransparent: true),
            _ => null,
        };

        using var dash = shape.Dashed ? SKPathEffect.CreateDash([6, 4], 0) : null;

        if (glow)
        {
            var sigma = shape.BorderBlurRadius > 0 ? (float)shape.BorderBlurRadius : t * 1.5f;
            using var halo = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = t * 2.4f,
                Color = shape.BorderColor.ToSkColor(),
                Shader = shader,
                PathEffect = dash,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma),
            };
            c.DrawPath(p, halo);
        }

        using var edge = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = t,
            StrokeJoin = SKStrokeJoin.Round,
            Color = shape.BorderColor.ToSkColor(),
            Shader = shader,
            PathEffect = dash,
        };

        if (!glow && shape.BorderBlurRadius > 0)
        {
            edge.ImageFilter = SKImageFilter.CreateBlur((float)shape.BorderBlurRadius, (float)shape.BorderBlurRadius);
        }

        c.DrawPath(p, edge);
    }

    /// <summary>Draws only the (offset, blurred) shadow of the path — never the path itself.</summary>
    private static void DropShadowOnly(SKCanvas c, SKPath p, float dx, float dy, float sigma, SKColor color)
    {
        using var filter = SKImageFilter.CreateDropShadowOnly(dx, dy, sigma, sigma, color);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black, ImageFilter = filter };
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
