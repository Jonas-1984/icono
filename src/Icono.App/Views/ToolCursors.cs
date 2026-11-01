using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Icono.App.Views;

/// <summary>The mouse cursors of the tools that pick or change something at one spot: the Color Picker, Region Color, Add Point and Delete Point.
/// Each cursor is the icon of its tool (the same drawing as on its key), with a white edge so it reads on dark and light drawings; its hot spot is
/// the spot of the icon that does the work (the tip of the pipette, the drop of the bucket, the tip of the arrow).</summary>
public static class ToolCursors
{
    private const int Size = 32;
    private const double IconScale = 1.25; // the 24 x 24 icon is drawn 30 x 30 in the 32 x 32 cursor
    private const double Margin = 1;

    private static Cursor? _picker;
    private static Cursor? _bucket;
    private static Cursor? _addPoint;
    private static Cursor? _deletePoint;

    public static Cursor ColorPicker => _picker ??= FromIcon("EyedropperToolGeometry", 2.3, 21.7);

    public static Cursor RegionColor => _bucket ??= FromIcon("RegionColorToolGeometry", 20.5, 21.0);

    public static Cursor AddPoint => _addPoint ??= FromIcon("AddPointToolGeometry", 4.2, 4.2);

    public static Cursor DeletePoint => _deletePoint ??= FromIcon("DeletePointToolGeometry", 4.2, 4.2);

    /// <summary>The cursor of the icon with the resource key <paramref name="key"/>; <paramref name="hotX"/> and <paramref name="hotY"/> are the spot
    /// that clicks, in the 24 x 24 box of the icon.</summary>
    private static Cursor FromIcon(string key, double hotX, double hotY)
    {
        if (Application.Current?.TryFindResource(key) is not Geometry icon)
        {
            return Cursors.Cross;
        }

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var halo = new Pen(Brushes.White, 4.2 / IconScale) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var ink = new Pen(new SolidColorBrush(Color.FromRgb(0x1B, 0x23, 0x32)), 2.0 / IconScale)
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
            };
            dc.PushTransform(new MatrixTransform(IconScale, 0, 0, IconScale, Margin, Margin));
            dc.DrawGeometry(null, halo, icon);
            dc.DrawGeometry(null, ink, icon);
            dc.Pop();
        }

        return CursorBuilder.Build(
            visual, Size, (int)Math.Round(hotX * IconScale + Margin), (int)Math.Round(hotY * IconScale + Margin));
    }
}
