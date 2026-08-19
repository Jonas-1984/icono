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
}
