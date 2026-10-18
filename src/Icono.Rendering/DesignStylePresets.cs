using Icono.Core;

namespace Icono.Rendering;

/// <summary>
/// Choosing a design style fills the shape's regular Border and Effects settings with values that suit that style
/// (see <see cref="DesignStyleRenderer"/>, which draws the style's edge from exactly these properties). From then on the
/// user can edit them in the BORDER and EFFECTS cards and the styled shape follows. Applying a style also clears the
/// drop shadow, so the style's own built-in shadow shows until the user switches the shadow on.
/// </summary>
public static class DesignStylePresets
{
    public static void Apply(Shape shape, DesignStyleKind kind)
    {
        shape.DesignStyle = kind;
        if (kind == DesignStyleKind.None)
        {
            return;
        }

        var m = Math.Max(1, Math.Min(shape.Width, shape.Height));
        var fill = shape.FillColor;

        shape.ShadowEnabled = false;
        shape.Dashed = false;
        shape.BorderBlurRadius = 0;
        shape.BorderStyleKind = BorderStyleKind.Solid;

        switch (kind)
        {
            case DesignStyleKind.Glassmorphism:
                shape.BorderThickness = Math.Clamp(m * 0.014, 1.2, 3);
                shape.BorderColor = new ArgbColor(235, 255, 255, 255);
                shape.BorderStyleKind = BorderStyleKind.Gradient;
                shape.BorderGradientColor = new ArgbColor(45, 255, 255, 255);
                break;

            case DesignStyleKind.Neumorphism:
            case DesignStyleKind.Claymorphism3D:
                shape.BorderThickness = 0;
                break;

            case DesignStyleKind.FluentDesign:
                shape.BorderThickness = 1.2;
                shape.BorderColor = new ArgbColor(110, 255, 255, 255);
                shape.BorderStyleKind = BorderStyleKind.Gradient;
                shape.BorderGradientColor = new ArgbColor(90, 0, 0, 0);
                break;

            case DesignStyleKind.AuroraGradients:
                shape.BorderThickness = Math.Clamp(m * 0.01, 1, 2);
                shape.BorderColor = new ArgbColor(85, 255, 255, 255);
                break;

            case DesignStyleKind.BentoUI:
                shape.BorderThickness = 1.2;
                shape.BorderColor = new ArgbColor(120, 255, 255, 255);
                shape.BorderStyleKind = BorderStyleKind.Gradient;
                shape.BorderGradientColor = new ArgbColor(150, (byte)(fill.R * 0.78), (byte)(fill.G * 0.78), (byte)(fill.B * 0.78));
                if (ShapeGeometryFactory.IsCornerRoundable(shape.Kind))
                {
                    shape.CornerRadius = Math.Round(m * 0.2);
                }

                break;

            case DesignStyleKind.Cyberpunk:
                shape.BorderThickness = Math.Clamp(m * 0.025, 1.6, 3.5);
                shape.BorderColor = ArgbColor.FromRgb(0x59, 0xEE, 0xFF);
                shape.BorderBlurRadius = Math.Round(shape.BorderThickness * 1.6, 1);
                break;

            case DesignStyleKind.DarkNeon:
                shape.BorderThickness = Math.Clamp(m * 0.022, 1.5, 3.5);
                shape.BorderColor = Neon(fill);
                shape.BorderBlurRadius = Math.Round(shape.BorderThickness * 1.8, 1);
                break;
        }
    }

    /// <summary>The saturated, fully bright version of a colour — the neon accent of the Dark UI style.</summary>
    private static ArgbColor Neon(ArgbColor color)
    {
        var sk = new SkiaSharp.SKColor(color.R, color.G, color.B);
        sk.ToHsv(out var h, out var s, out _);
        var neon = SkiaSharp.SKColor.FromHsv(h, Math.Max(s, 82f), 100f);
        return ArgbColor.FromRgb(neon.Red, neon.Green, neon.Blue);
    }
}
