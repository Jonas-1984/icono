using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Icono.App.Views;

/// <summary>The mouse cursor of the Curve and the Match tool: a small square (the pick box of a CAD program) with its middle on the hot spot.
/// It is drawn once, with a white edge so it reads on dark and light drawings, and built as a cursor file in memory.</summary>
public static class PickBoxCursor
{
    private const int Size = 32;

    private static Cursor? _instance;

    public static Cursor Instance => _instance ??= Build();

    private static Cursor Build()
    {
        var visual = new DrawingVisual();
        RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);
        using (var dc = visual.RenderOpen())
        {
            var square = new Rect(10, 10, 12, 12);
            var halo = new Pen(Brushes.White, 4);
            var ink = new Pen(Brushes.Black, 2);
            dc.DrawRoundedRectangle(null, halo, square, 2, 2);
            dc.DrawRoundedRectangle(null, ink, square, 2, 2);
        }

        return CursorBuilder.Build(visual, Size, Size / 2, Size / 2);
    }
}
