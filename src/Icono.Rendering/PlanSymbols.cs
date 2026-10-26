using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>
/// The symbols of an electrical and kitchen plan: socket, double socket, electrical appliance, cooker, oven, microwave, the connections for water
/// and waste water, and the cooker hood. They are placed at a point and, when only clicked, drawn at their real size at the drawing scale. Their
/// outlines are 24 x 24 templates like the other kinds; the solid parts (dots, the E) are drawn on top in the border colour.
/// </summary>
public static class PlanSymbols
{
    public static bool IsPlanSymbol(ShapeKind kind) =>
        kind is ShapeKind.Socket or ShapeKind.DoubleSocket or ShapeKind.ElectricAppliance or ShapeKind.Cooker or ShapeKind.Oven
            or ShapeKind.Microwave or ShapeKind.WaterAndWastewater or ShapeKind.Hood;

    /// <summary>The outline of a symbol as SVG path data in its 24 x 24 template.</summary>
    public static string OutlineData(ShapeKind kind) => kind switch
    {
        ShapeKind.ElectricAppliance => "M3,3 H21 V21 H3 Z",
        ShapeKind.Cooker => "M3,3 H21 V21 H3 Z",
        ShapeKind.Oven => "M3,4 H21 V20 H3 Z M3,8.5 H21",
        ShapeKind.Microwave =>
            "M7,3 H17 A4,4 0 0 1 21,7 V17 A4,4 0 0 1 17,21 H7 A4,4 0 0 1 3,17 V7 A4,4 0 0 1 7,3 Z " +
            Wave(8.5) + Wave(12) + Wave(15.5),
        _ => string.Empty,
    };

    /// <summary>The solid parts of a symbol (drawn filled in the border colour), or null.</summary>
    public static string? SolidData(ShapeKind kind) => kind switch
    {
        ShapeKind.ElectricAppliance => "M8,7 H10.4 V17 H8 Z M8,7 H16 V9.2 H8 Z M8,11 H15 V13.2 H8 Z M8,14.8 H16 V17 H8 Z",
        ShapeKind.Cooker => Circle(12, 8.5, 1.8) + Circle(8, 15.5, 1.8) + Circle(16, 15.5, 1.8),
        ShapeKind.Oven => Circle(12, 14.5, 1.9),
        _ => null,
    };


    // ----- symbols taken from the drawing "installations.svg" (draw.io, scale 4) ---------------------------------------

    /// <summary>The socket, the double socket, the water and waste water connections and the hood exactly as drawn in the file: coordinates of the
    /// export (4 units per drawing unit). Outline = stroked lines and circles, Solid = parts filled in the colour of the line.</summary>
    private static (string Outline, string? Solid)? Drawing(ShapeKind kind) => kind switch
    {
        // a bar on top of a half circle, a stem on the bar
        ShapeKind.Socket => ("M 202 642 L 202 722 M 162 722 L 242 722 M 162 762 A 40 40 0 0 1 242 762", null),

        // the socket with a 2 to the right of its stem
        ShapeKind.DoubleSocket => (
            "M 202 642 L 202 722 M 162 722 L 242 722 M 162 762 A 40 40 0 0 1 242 762 " +
            "M 211 666 C 211 654 237 654 237 668 C 237 678 219 688 211 702 L 238 702", null),

        // a small circle with a cross, a big circle, a small circle
        ShapeKind.WaterAndWastewater => (
            "M 2 82 A 40 40 0 1 0 82 82 A 40 40 0 1 0 2 82 Z " +
            "M 122 82 A 80 80 0 1 0 282 82 A 80 80 0 1 0 122 82 Z " +
            "M 322 82 A 40 40 0 1 0 402 82 A 40 40 0 1 0 322 82 Z " +
            "M 13.72 110.28 L 70.28 53.72 M 70.28 110.28 L 13.72 53.72", null),

        // a circle with two opposite quarters filled
        ShapeKind.Hood => (
            "M 762 82 A 80 80 0 1 0 922 82 A 80 80 0 1 0 762 82 Z",
            "M 842 82 L 922 82 A 80 80 0 0 1 842 162 Z M 842 82 L 762 82 A 80 80 0 0 1 842 2 Z"),

        _ => null,
    };

    private static readonly Dictionary<ShapeKind, (SKPath Outline, SKPath? Solid)> Fitted = [];

    /// <summary>The drawing of a kind fitted into the 24 x 24 template (the longer side 22 long, centred), or null for kinds without a drawing. The paths
    /// are shared: callers copy them.</summary>
    private static (SKPath Outline, SKPath? Solid)? FromDrawing(ShapeKind kind)
    {
        lock (Fitted)
        {
            if (Fitted.TryGetValue(kind, out var known))
            {
                return known;
            }

            if (Drawing(kind) is not { } drawing)
            {
                return null;
            }

            var outline = SKPath.ParseSvgPathData(drawing.Outline) ?? new SKPath();
            var solid = drawing.Solid is null ? null : SKPath.ParseSvgPathData(drawing.Solid);
            var bounds = outline.Bounds;
            if (solid is not null)
            {
                bounds.Union(solid.Bounds);
            }

            var scale = 22f / Math.Max(bounds.Width, bounds.Height);
            var matrix = SKMatrix.CreateScale(scale, scale)
                .PostConcat(SKMatrix.CreateTranslation(12 - bounds.MidX * scale, 12 - bounds.MidY * scale));
            outline.Transform(matrix);
            solid?.Transform(matrix);

            var result = (outline, solid);
            Fitted[kind] = result;
            return result;
        }
    }

    /// <summary>The outline of a symbol in its 24 x 24 template (a new path the caller disposes).</summary>
    public static SKPath CreateOutline(ShapeKind kind) =>
        FromDrawing(kind) is { } fitted ? new SKPath(fitted.Outline) : SKPath.ParseSvgPathData(OutlineData(kind)) ?? new SKPath();

    /// <summary>The solid parts of a symbol in its 24 x 24 template (a new path the caller disposes), or null.</summary>
    public static SKPath? CreateSolid(ShapeKind kind) =>
        FromDrawing(kind) is { } fitted ? (fitted.Solid is null ? null : new SKPath(fitted.Solid)) : SolidData(kind) is { } data ? SKPath.ParseSvgPathData(data) : null;

    private static string Circle(double x, double y, double r) =>
        FormattableString.Invariant($"M{x - r},{y} A{r},{r} 0 1 0 {x + r},{y} A{r},{r} 0 1 0 {x - r},{y} Z ");

    private static string Wave(double y) =>
        FormattableString.Invariant($"M6.5,{y} C8.2,{y - 2.2} 10.3,{y - 2.2} 12,{y} S15.8,{y + 2.2} 17.5,{y} ");

    /// <summary>What a freshly placed symbol looks like: a white body with a dark outline like the doors and windows; the sockets, the water connections and the hood keep the colours of their drawing.</summary>
    public static void ApplyDefaults(Shape shape)
    {
        shape.FillColor = ArgbColor.FromRgb(0xFF, 0xFF, 0xFF);
        shape.BorderColor = ArgbColor.FromRgb(0x23, 0x29, 0x33);
        shape.BorderThickness = 1.2;

        // the colours of the drawing the symbols come from: red for the sockets, blue for water, green for the hood
        switch (shape.Kind)
        {
            case ShapeKind.Socket:
            case ShapeKind.DoubleSocket:
                shape.BorderColor = ArgbColor.FromRgb(0xFF, 0x00, 0x00);
                shape.FillEnabled = false;
                break;
            case ShapeKind.WaterAndWastewater:
                shape.BorderColor = ArgbColor.FromRgb(0x00, 0x66, 0xCC);
                break;
            case ShapeKind.Hood:
                shape.BorderColor = ArgbColor.FromRgb(0x97, 0xD0, 0x77);
                shape.FillEnabled = false;
                break;
        }
    }

    /// <summary>The real size of a symbol in cm (the width of what is drawn).</summary>
    private static double Centimetres(ShapeKind kind) => kind switch
    {
        ShapeKind.Socket => 20,
        ShapeKind.DoubleSocket => 26,
        ShapeKind.Microwave => 45,
        ShapeKind.WaterAndWastewater => 60,
        _ => 60,
    };

    /// <summary>The size (px) of the outline of a symbol that is only clicked: its real width at the drawing scale, in the proportion of the symbol.</summary>
    public static (double Width, double Height) DefaultSize(ShapeKind kind, double scaleDenominator, double pageWidth)
    {
        var width = Centimetres(kind) / 2.54 * 96 / Math.Max(1, scaleDenominator);
        width = Math.Clamp(width, 18, Math.Max(18, pageWidth * 0.3));

        var bounds = ShapeGeometryFactory.LocalBounds(kind);
        var aspect = bounds.Width > 0.01f ? bounds.Height / bounds.Width : 1;
        return (width, width * aspect);
    }
}
