namespace Icono.Core;

public readonly record struct ArgbColor(byte A, byte R, byte G, byte B)
{
    public static ArgbColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);
}
