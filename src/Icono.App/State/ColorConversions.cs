using Icono.Core;
using MediaColor = System.Windows.Media.Color;

namespace Icono.App.State;

public static class ColorConversions
{
    public static MediaColor ToMediaColor(this ArgbColor color) =>
        MediaColor.FromArgb(color.A, color.R, color.G, color.B);

    public static ArgbColor FromMediaColor(this MediaColor color) =>
        new(color.A, color.R, color.G, color.B);

    /// <summary>h: 0-360, s: 0-1, v: 0-1.</summary>
    public static MediaColor FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        var m = v - c;

        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return MediaColor.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    public static (double H, double S, double V) ToHsv(this MediaColor color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double h;
        if (delta < 0.0001) h = 0;
        else if (max == r) h = 60 * ((g - b) / delta % 6);
        else if (max == g) h = 60 * ((b - r) / delta + 2);
        else h = 60 * ((r - g) / delta + 4);
        if (h < 0) h += 360;

        var s = max <= 0 ? 0 : delta / max;
        return (h, s, max);
    }
}
