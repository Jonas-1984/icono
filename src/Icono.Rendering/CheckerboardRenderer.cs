using SkiaSharp;

namespace Icono.Rendering;

public static class CheckerboardRenderer
{
    private const int CellSize = 8;
    private static readonly SKColor LightCell = new(0xFF, 0xFF, 0xFF);
    private static readonly SKColor DarkCell = new(0xD9, 0xD9, 0xD9);

    public static void Render(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(LightCell);

        using var paint = new SKPaint { Color = DarkCell, IsAntialias = false };

        for (var y = 0; y < height; y += CellSize)
        {
            for (var x = 0; x < width; x += CellSize)
            {
                var isDark = ((x / CellSize) + (y / CellSize)) % 2 == 1;
                if (!isDark) continue;

                canvas.DrawRect(SKRect.Create(x, y, CellSize, CellSize), paint);
            }
        }
    }
}
