using System.Collections.ObjectModel;

namespace Icono.Core;

public sealed class Document
{
    public const int PreviewSize = 256;

    /// <summary>DIN A4 portrait (210 x 297 mm) at the editor's 96 DPI — the page a new session starts with.</summary>
    public const int A4Width = 794;
    public const int A4Height = 1123;

    /// <summary>Width of every page.</summary>
    public int Width { get; set; } = PreviewSize;

    /// <summary>Height of every page (the whole drawing is <see cref="TotalHeight"/> high when there are several pages).</summary>
    public int Height { get; set; } = PreviewSize;

    public const int MaxPages = 50;

    /// <summary>Space between two pages (px). The pages lie below each other in one drawing space: page <c>i</c> starts at <see cref="PageTop"/>.</summary>
    public const int PageGap = 48;

    private int _pageCount = 1;

    /// <summary>How many pages the project has (1 to <see cref="MaxPages"/>), one below the other.</summary>
    public int PageCount { get => _pageCount; set => _pageCount = Math.Clamp(value, 1, MaxPages); }

    /// <summary>The y of the top edge of page <paramref name="index"/> (0 = first page).</summary>
    public int PageTop(int index) => Math.Clamp(index, 0, PageCount - 1) * (Height + PageGap);

    /// <summary>The height of all pages with the gaps between them: the extent of the drawing space.</summary>
    public int TotalHeight => PageCount * Height + (PageCount - 1) * PageGap;

    /// <summary>The page a y belongs to: the page it lies in, or the nearest one when it is in a gap or outside.</summary>
    public int PageIndexAt(double y)
    {
        var step = Height + PageGap;
        var index = (int)Math.Floor((y + PageGap / 2.0) / step);
        return Math.Clamp(index, 0, PageCount - 1);
    }

    /// <summary>The drawing scale 1:N (N = this value; 1 = full size). The page is the paper: at 1:50 one cm on the page stands for 50 cm in
    /// reality, so measurements, wall sizes and dimensions are written in real-world units, and printing the page at 100% gives the exact scale.</summary>
    public double ScaleDenominator { get; set; } = 1;

    /// <summary>The frame and title block of the page (see <see cref="SheetInfo"/>).</summary>
    public SheetInfo Sheet { get; set; } = new();

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
