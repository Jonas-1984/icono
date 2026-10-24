using Icono.Core;

namespace Icono.Rendering;

/// <summary>The drawing scale 1:N of the document being drawn. Dimension and wall texts read it to write real-world lengths
/// (page length times N). It is set whenever a document is rendered or redrawn.</summary>
public static class DrawingScale
{
    private static double _denominator = 1;

    public static double Denominator
    {
        get => _denominator;
        set => _denominator = value > 0 ? value : 1;
    }

    /// <summary>Menu and drop-down text of a preset scale: "1:50", and "1:1  (full size)".</summary>
    public static string PresetLabel(double denominator) => denominator == 1 ? "1:1  (full size)" : Label(denominator);

    /// <summary>"1:50", or "1:1" at full size.</summary>
    public static string Label(double denominator) => "1:" + denominator.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
