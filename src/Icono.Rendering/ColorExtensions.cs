using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

public static class ColorExtensions
{
    public static SKColor ToSkColor(this ArgbColor color) => new(color.R, color.G, color.B, color.A);
}
