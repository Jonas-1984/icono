using Icono.Core;

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
        // a half circle on a line, a bar from the line to the top of the half circle
        ShapeKind.Socket => "M6,15 A6,6 0 0 1 18,15 M3,15 H21 M12,15 V9",
        ShapeKind.DoubleSocket => "M4,15 A6,6 0 0 1 16,15 M2,15 H18 M10,15 V9 M18.5,9.5 H21.5 V12 H18.5 V14.5 H21.5",
        ShapeKind.ElectricAppliance => "M3,3 H21 V21 H3 Z",
        ShapeKind.Cooker => "M3,3 H21 V21 H3 Z",
        ShapeKind.Oven => "M3,4 H21 V20 H3 Z M3,8.5 H21",
        ShapeKind.Microwave =>
            "M7,3 H17 A4,4 0 0 1 21,7 V17 A4,4 0 0 1 17,21 H7 A4,4 0 0 1 3,17 V7 A4,4 0 0 1 7,3 Z " +
            Wave(8.5) + Wave(12) + Wave(15.5),
        // a small, a big and a small circle in a row
        ShapeKind.WaterAndWastewater => Circle(3.25, 12, 2.25) + Circle(12, 12, 4.5) + Circle(20.75, 12, 2.25),
        _ => Circle(12, 12, 8.5) + " M12,3.5 V20.5 M3.5,12 H20.5", // Hood
    };

    /// <summary>The solid parts of a symbol (drawn filled in the border colour), or null.</summary>
    public static string? SolidData(ShapeKind kind) => kind switch
    {
        ShapeKind.ElectricAppliance => "M8,7 H10.4 V17 H8 Z M8,7 H16 V9.2 H8 Z M8,11 H15 V13.2 H8 Z M8,14.8 H16 V17 H8 Z",
        ShapeKind.Cooker => Circle(12, 8.5, 1.8) + Circle(8, 15.5, 1.8) + Circle(16, 15.5, 1.8),
        ShapeKind.Oven => Circle(12, 14.5, 1.9),
        _ => null,
    };

    private static string Circle(double x, double y, double r) =>
        FormattableString.Invariant($"M{x - r},{y} A{r},{r} 0 1 0 {x + r},{y} A{r},{r} 0 1 0 {x - r},{y} Z ");

    private static string Wave(double y) =>
        FormattableString.Invariant($"M6.5,{y} C8.2,{y - 2.2} 10.3,{y - 2.2} 12,{y} S15.8,{y + 2.2} 17.5,{y} ");

    /// <summary>What a freshly placed symbol looks like: a white body with a dark outline, like the doors and windows.</summary>
    public static void ApplyDefaults(Shape shape)
    {
        shape.FillColor = ArgbColor.FromRgb(0xFF, 0xFF, 0xFF);
        shape.BorderColor = ArgbColor.FromRgb(0x23, 0x29, 0x33);
        shape.BorderThickness = 1.2;
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
