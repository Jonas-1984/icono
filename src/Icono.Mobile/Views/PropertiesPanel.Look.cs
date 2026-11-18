using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Icono.Core;
using Icono.Editor;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Mobile.Views;

/// <summary>The look of the selected objects as the cards of the Windows program have it: size and position, the turn, the border (style, gradient,
/// dashes, blur, corners, presets), opacity / blur / fade / shadow, the ends and routing of a line, the depth. Everything is stored in the
/// <see cref="Shape"/> the same way the Windows program does, so a drawing looks the same on both.</summary>
public sealed partial class PropertiesPanel
{
    /// <summary>1 cm on the paper, in page pixels (96 per inch).</summary>
    private const double LinePxPerCm = 96 / 2.54;

    /// <summary>The named colours of the border presets (the same as in the Windows program).</summary>
    private static readonly ArgbColor[] PresetColors =
    [
        ArgbColor.FromRgb(0xFF, 0xFF, 0xFF), ArgbColor.FromRgb(0x2B, 0x2B, 0x2B), ArgbColor.FromRgb(0xB0, 0xB0, 0xB0), ArgbColor.FromRgb(0xD8, 0xDC, 0xE0),
        ArgbColor.FromRgb(0xE5, 0x73, 0x73), ArgbColor.FromRgb(0xC6, 0x28, 0x28), ArgbColor.FromRgb(0xFF, 0xB7, 0x4D), ArgbColor.FromRgb(0xF4, 0x51, 0x1E),
        ArgbColor.FromRgb(0xFF, 0xD5, 0x4F), ArgbColor.FromRgb(0xFF, 0xF1, 0x76), ArgbColor.FromRgb(0xC0, 0xCA, 0x33), ArgbColor.FromRgb(0x81, 0xC7, 0x84),
        ArgbColor.FromRgb(0x38, 0x8E, 0x3C), ArgbColor.FromRgb(0x4D, 0xB6, 0xAC), ArgbColor.FromRgb(0x00, 0x79, 0x6B), ArgbColor.FromRgb(0x4D, 0xD0, 0xE1),
        ArgbColor.FromRgb(0x4F, 0xC3, 0xF7), ArgbColor.FromRgb(0x64, 0xB5, 0xF6), ArgbColor.FromRgb(0x1E, 0x88, 0xE5), ArgbColor.FromRgb(0x5C, 0x6B, 0xC0),
        ArgbColor.FromRgb(0xBA, 0x68, 0xC8), ArgbColor.FromRgb(0x7E, 0x57, 0xC2), ArgbColor.FromRgb(0xF0, 0x62, 0x92), ArgbColor.FromRgb(0xEC, 0x40, 0x7A),
        ArgbColor.FromRgb(0xA1, 0x88, 0x7F), ArgbColor.FromRgb(0x6D, 0x4C, 0x41), ArgbColor.FromRgb(0xE6, 0xD3, 0xB3), ArgbColor.FromRgb(0xA8, 0xE6, 0xCF),
        ArgbColor.FromRgb(0x3D, 0x6E, 0x8C), ArgbColor.FromRgb(0xFF, 0xE6, 0x8A), ArgbColor.FromRgb(0xFF, 0x8A, 0x80),
    ];

    private static readonly (string Name, ArrowHeadKind Value)[] ArrowHeads =
    [
        ("None", ArrowHeadKind.None), ("Open arrow", ArrowHeadKind.Open), ("Filled arrow", ArrowHeadKind.Filled), ("Thin arrow", ArrowHeadKind.Thin),
        ("Half arrow", ArrowHeadKind.Half), ("Circle", ArrowHeadKind.Circle), ("Diamond", ArrowHeadKind.Diamond), ("Square", ArrowHeadKind.Square),
        ("Bar", ArrowHeadKind.Bar),
    ];

    private static readonly (string Name, FadeDirection Value)[] FadeDirections =
    [
        ("Top → Bottom", FadeDirection.TopToBottom), ("Bottom → Top", FadeDirection.BottomToTop), ("Left → Right", FadeDirection.LeftToRight),
        ("Right → Left", FadeDirection.RightToLeft), ("Both sides (vertical)", FadeDirection.VerticalBothSides),
        ("Both sides (horizontal)", FadeDirection.HorizontalBothSides),
    ];

    // ---------------------------------------------------------------- size, position, turn

    /// <summary>X, Y, width and height of ONE object (in cm at the drawing scale - what is drawn, not the frame round it) and its turn.</summary>
    private void BuildPosition(EditorContext context, IReadOnlyList<Shape> shapes)
    {
        var shape = shapes[0];
        Heading("SIZE & POSITION");
        if (shapes.Count == 1)
        {
            var box = ShapeOutline.Box(shape);

            void Set(Func<(double X, double Y, double W, double H), (double X, double Y, double W, double H)> change) =>
                context.SetOnSelection(s =>
                {
                    var b = change(ShapeOutline.Box(s));
                    ShapeOutline.SetBox(s, b.X, b.Y, Math.Max(1, b.W), Math.Max(1, b.H));
                }, s => ReferenceEquals(s, shape));

            NumberRow("X", ToRealCm(box.X), "cm", 1, -100000, 100000, "0.#", cm => Set(b => (FromRealCm(cm), b.Y, b.W, b.H)));
            NumberRow("Y", ToRealCm(box.Y), "cm", 1, -100000, 100000, "0.#", cm => Set(b => (b.X, FromRealCm(cm), b.W, b.H)));
            NumberRow("Width", ToRealCm(box.W), "cm", 1, 0.1, 100000, "0.#", cm => Set(b => (b.X, b.Y, FromRealCm(cm), b.H)));
            NumberRow("Height", ToRealCm(box.H), "cm", 1, 0.1, 100000, "0.#", cm => Set(b => (b.X, b.Y, b.W, FromRealCm(cm))));
        }

        NumberRow("Turn", shape.Rotation, "°", 15, 0, 360, "0.#", angle => context.SetOnSelection(s => s.Rotation = ((angle % 360) + 360) % 360));
        if (shapes.Any(s => s.Kind is not (ShapeKind.Wall or ShapeKind.Dimension)))
        {
            NumberRow("Depth (3D)", shape.Depth3D, "px", 2, 0, 60, "0.#", d => context.SetOnSelection(s => s.Depth3D = d));
        }
    }

    // ---------------------------------------------------------------- border / line

    /// <summary>The border (a line for lines): thickness, colour, style (solid, gradient, fade), dashes, blur, corners and the style presets.</summary>
    private void BuildBorder(EditorContext context, IReadOnlyList<Shape> outlined)
    {
        var sample = outlined[0];
        var isLine = outlined.All(s => ShapeGeometryFactory.IsLineFamily(s.Kind));
        bool Applies(Shape s) => outlined.Contains(s);

        Heading(isLine ? "LINE" : "BORDER");
        NumberRow("Width", sample.BorderThickness / PxPerCm * 10, "mm", 0.1, 0, 60, "0.0",
            mm => context.SetOnSelection(s => s.BorderThickness = mm / 10 * PxPerCm, Applies));
        ColorRow(sample.BorderColor, c => context.SetOnSelection(s => s.BorderColor = c, Applies));
        Choice("Style", [("Solid", BorderStyleKind.Solid), ("Gradient", BorderStyleKind.Gradient), ("Fade", BorderStyleKind.Fade)], sample.BorderStyleKind,
            style => context.SetOnSelection(s => s.BorderStyleKind = style, Applies));
        if (sample.BorderStyleKind == BorderStyleKind.Gradient)
        {
            Note("Second colour of the gradient:");
            ColorRow(sample.BorderGradientColor, c => context.SetOnSelection(s => s.BorderGradientColor = c, Applies));
        }

        Toggle("Dashed", sample.Dashed, on => context.SetOnSelection(s => s.Dashed = on, Applies));
        NumberRow("Blur", sample.BorderBlurRadius, "px", 1, 0, 30, "0.#", v => context.SetOnSelection(s => s.BorderBlurRadius = v, Applies));
        if (isLine)
        {
            Toggle("Rounded corners", sample.CornerRadius > 0,
                on => context.SetOnSelection(s => s.CornerRadius = on ? Math.Max(s.CornerRadius, 0.3 * LinePxPerCm) : 0, Applies));
            if (sample.CornerRadius > 0)
            {
                NumberRow("Radius", sample.CornerRadius / LinePxPerCm, "cm", 0.1, 0, 20, "0.0#",
                    cm => context.SetOnSelection(s => s.CornerRadius = cm * LinePxPerCm, Applies));
            }
        }
        else
        {
            NumberRow("Corners", sample.CornerRadius, "px", 2, 0, 50, "0.#", v => context.SetOnSelection(s => s.CornerRadius = v, Applies));
        }

        Note("Style presets (border colour):");
        ColorRow(sample.BorderColor, c => context.SetOnSelection(s => s.BorderColor = c, Applies), PresetColors, hexEntry: false);
        ButtonRow(("Acrylic look", () => context.SetOnSelection(AcrylicLook, Applies)));
    }

    /// <summary>Frosted glass: a light translucent fill, a soft blur, a fade for the highlight and a light gradient rim (as the Windows preset).</summary>
    private static void AcrylicLook(Shape shape)
    {
        shape.FillColor = ArgbColor.FromRgb(0xFF, 0xFF, 0xFF);
        shape.Opacity = 55;
        shape.BlurRadius = 3;
        shape.FadeAmount = 25;
        shape.BorderStyleKind = BorderStyleKind.Gradient;
        shape.BorderColor = ArgbColor.FromRgb(0xFF, 0xFF, 0xFF);
        shape.BorderGradientColor = ArgbColor.FromRgb(0xC8, 0xE0, 0xFF);
        shape.BorderBlurRadius = 2;
    }

    // ---------------------------------------------------------------- opacity, blur, fade, shadow

    private void BuildEffects(EditorContext context, IReadOnlyList<Shape> shapes)
    {
        var sample = shapes[0];
        Heading("OPACITY, BLUR & SHADOW");
        NumberRow("Opacity", sample.Opacity, "%", 5, 0, 100, "0", v => context.SetOnSelection(s => s.Opacity = v));
        NumberRow("Blur", sample.BlurRadius, "px", 1, 0, 30, "0.#", v => context.SetOnSelection(s => s.BlurRadius = v));
        NumberRow("Fade", sample.FadeAmount, "%", 5, 0, 100, "0", v => context.SetOnSelection(s => s.FadeAmount = v));
        if (sample.FadeAmount > 0)
        {
            Choice("Fade way", FadeDirections, sample.FadeDirection, d => context.SetOnSelection(s => s.FadeDirection = d));
        }

        Toggle("Shadow", sample.ShadowEnabled, on => context.SetOnSelection(s => s.ShadowEnabled = on));
        if (sample.ShadowEnabled)
        {
            NumberRow("Distance", sample.ShadowDistance, "px", 1, 0, 30, "0.#", v => context.SetOnSelection(s => s.ShadowDistance = v));
            NumberRow("Strength", sample.ShadowStrength, "%", 5, 0, 100, "0", v => context.SetOnSelection(s => s.ShadowStrength = v));
            NumberRow("Softness", sample.ShadowBlurRadius, "px", 1, 0, 30, "0.#", v => context.SetOnSelection(s => s.ShadowBlurRadius = v));
            NumberRow("Direction", sample.ShadowAngle, "°", 15, 0, 360, "0", v => context.SetOnSelection(s => s.ShadowAngle = v));
            ColorRow(sample.ShadowColor, c => context.SetOnSelection(s => s.ShadowColor = c));
        }
    }

    // ---------------------------------------------------------------- line ends and routing

    private void BuildLineEnds(EditorContext context, IReadOnlyList<Shape> shapes)
    {
        var lines = shapes.Where(s => ShapeGeometryFactory.HasLineEnds(s.Kind)).ToList();
        if (lines.Count == 0)
        {
            return;
        }

        var sample = lines[0];
        bool Applies(Shape s) => lines.Contains(s);
        Heading("LINE ENDS");
        Choice("Start", ArrowHeads, sample.LineStartHead, h => context.SetOnSelection(s => s.LineStartHead = h, Applies));
        Choice("End", ArrowHeads, sample.LineEndHead, h => context.SetOnSelection(s => s.LineEndHead = h, Applies));
        Choice("Path", [("Straight", PathRoutingStyle.Straight), ("Right angles", PathRoutingStyle.Orthogonal), ("Rounded", PathRoutingStyle.Rounded),
            ("Curved", PathRoutingStyle.Curved)], sample.PathRoutingStyle, r => context.SetOnSelection(s => s.PathRoutingStyle = r, Applies));
    }

    // ---------------------------------------------------------------- text formatting (the rest of the TEXT section)

    /// <summary>The fonts the tablet has (plus the default of the drawing files): the family of the text.</summary>
    private static IReadOnlyList<(string Name, string Value)> FontFamilies()
    {
        var names = new List<string> { "Segoe UI" };
        try
        {
            names.AddRange(SKFontManager.Default.FontFamilies.Where(f => !string.IsNullOrWhiteSpace(f)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException)
        {
            // no font list: only the default is offered
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(n => (n, n)).ToList();
    }

    private void BuildTextFormat(EditorContext context, IReadOnlyList<Shape> withText, Func<Shape, bool> hasText)
    {
        var sample = withText[0];
        var family = string.IsNullOrWhiteSpace(sample.TextFontFamily) ? "Segoe UI" : sample.TextFontFamily!;
        var fonts = FontFamilies().ToList();
        if (!fonts.Any(f => string.Equals(f.Value, family, StringComparison.OrdinalIgnoreCase)))
        {
            fonts.Insert(0, (family, family));
        }

        Choice("Font", fonts, fonts.First(f => string.Equals(f.Value, family, StringComparison.OrdinalIgnoreCase)).Value,
            name => context.SetOnSelection(s => s.TextFontFamily = name, hasText));
        Toggle("Underline", sample.TextUnderline, on => context.SetOnSelection(s => s.TextUnderline = on, hasText));
        Choice("Align", [("Left", TextHorizontalAlign.Left), ("Centre", TextHorizontalAlign.Center), ("Right", TextHorizontalAlign.Right),
            ("Justified", TextHorizontalAlign.Justify)], sample.TextHAlign, a => context.SetOnSelection(s => s.TextHAlign = a, hasText));
        Choice("Position", [("Top", TextVerticalAlign.Top), ("Middle", TextVerticalAlign.Middle), ("Bottom", TextVerticalAlign.Bottom)],
            sample.TextVAlign, a => context.SetOnSelection(s => s.TextVAlign = a, hasText));
        if (withText.Any(s => s.Kind != ShapeKind.Text))
        {
            Choice("Place", [("Inside", TextPlacement.Inside), ("Below", TextPlacement.Below), ("Above", TextPlacement.Above), ("Left", TextPlacement.Left),
                ("Right", TextPlacement.Right)], sample.TextPlacement,
                p => context.SetOnSelection(s => s.TextPlacement = p, s => hasText(s) && s.Kind != ShapeKind.Text));
        }
    }
}
