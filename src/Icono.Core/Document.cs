using System.Collections.ObjectModel;

namespace Icono.Core;

public sealed class Document
{
    public const int PreviewSize = 256;

    /// <summary>DIN A4 portrait (210 x 297 mm) at the editor's 96 DPI — the page a new session starts with.</summary>
    public const int A4Width = 794;
    public const int A4Height = 1123;

    public int Width { get; set; } = PreviewSize;
    public int Height { get; set; } = PreviewSize;

    /// <summary>The drawing scale 1:N (N = this value; 1 = full size). The page is the paper: at 1:50 one cm on the page stands for 50 cm in
    /// reality, so measurements, wall sizes and dimensions are written in real-world units, and printing the page at 100% gives the exact scale.</summary>
    public double ScaleDenominator { get; set; } = 1;

    /// <summary>The scales offered in the menus (1:N).</summary>
    public static readonly double[] ScalePresets = [1, 2, 5, 10, 20, 25, 40, 50, 60, 75, 100, 200];


    public static readonly int[] ExportSizes = [64, 128, 256, 512];

    /// <summary>Index 0 = front-most layer (drawn last / on top).</summary>
    public ObservableCollection<Layer> Layers { get; } = new();

    /// <summary>Every shape of every visible layer, front-most first.</summary>
    public IEnumerable<Shape> VisibleShapes() => Layers.Where(l => l.Visible).SelectMany(l => l.Shapes);

    /// <summary>Preview-only; never affects exported output.</summary>
    public BackgroundMode BackgroundMode { get; set; } = BackgroundMode.Transparent;

    public ArgbColor BackgroundColor { get; set; } = ArgbColor.FromRgb(0xFF, 0xFF, 0xFF);

    /// <summary>PNG bytes of the picture used by <see cref="BackgroundMode.Image"/> (null = none chosen yet). Also the
    /// colour shown behind it is <see cref="BackgroundColor"/>.</summary>
    public byte[]? BackgroundImageBytes { get; set; }

    public string? BackgroundImageName { get; set; }

    public BackgroundImageFit BackgroundImageFit { get; set; } = BackgroundImageFit.Cover;

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
