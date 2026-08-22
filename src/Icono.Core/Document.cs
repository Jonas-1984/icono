using System.Collections.ObjectModel;

namespace Icono.Core;

public sealed class Document
{
    public const int PreviewSize = 256;

    public int Width { get; set; } = PreviewSize;
    public int Height { get; set; } = PreviewSize;

    public static readonly int[] ExportSizes = [64, 128, 256, 512];

    /// <summary>Index 0 = front-most layer (drawn last / on top).</summary>
    public ObservableCollection<Layer> Layers { get; } = new();

    /// <summary>Preview-only; never affects exported output.</summary>
    public BackgroundMode BackgroundMode { get; set; } = BackgroundMode.Transparent;

    public ArgbColor BackgroundColor { get; set; } = ArgbColor.FromRgb(0xFF, 0xFF, 0xFF);

    /// <summary>Base color of the <see cref="BackgroundMode.Blur"/> preview backdrop.</summary>
    public ArgbColor BackgroundBlurColor { get; set; } = ArgbColor.FromRgb(0x6F, 0xB1, 0xFF);

    /// <summary>When true, the blur backdrop fades from <see cref="BackgroundBlurColor"/> to
    /// <see cref="BackgroundBlurGradientColor"/> instead of staying one flat color.</summary>
    public bool BackgroundBlurGradientEnabled { get; set; }

    public ArgbColor BackgroundBlurGradientColor { get; set; } = ArgbColor.FromRgb(0xB3, 0x88, 0xFF);

    /// <summary>0-100. Gaussian blur strength of the backdrop.</summary>
    public double BackgroundBlurAmount { get; set; } = 60;

    /// <summary>0-100. How much the backdrop fades out toward the canvas edges (0 = flat, uniform
    /// coverage edge to edge; 100 = strongly vignetted, only the center stays fully colored).</summary>
    public double BackgroundFadeAmount { get; set; } = 40;

    /// <summary>0-100. Overall alpha of the blur backdrop.</summary>
    public double BackgroundOpacity { get; set; } = 70;
}
