namespace Icono.Core;

public sealed class Document
{
    public const int PreviewSize = 256;

    public int Width { get; set; } = PreviewSize;
    public int Height { get; set; } = PreviewSize;

    public static readonly int[] ExportSizes = [64, 128, 256, 512];
}
