using SkiaSharp;

namespace Icono.Rendering;

/// <summary>The small symbol that shows which kind of point was snapped to: a square for an end, a triangle for a middle, a circle for a centre, a cross
/// for a crossing ... (the symbols of the Windows program). Drawn around (<paramref name="x"/>, <paramref name="y"/>) in screen units with the pen given.</summary>
public static class PointSnapMarks
{
    public static void Draw(SKCanvas canvas, PointSnapKind kind, float x, float y, SKPaint paint)
    {
        const float h = 6f;
        switch (kind)
        {
            case PointSnapKind.Endpoint:
                canvas.DrawRect(x - h, y - h, 2 * h, 2 * h, paint);
                break;
            case PointSnapKind.Midpoint:
            {
                using var triangle = new SKPath();
                triangle.MoveTo(x, y - 7);
                triangle.LineTo(x + 7, y + 6);
                triangle.LineTo(x - 7, y + 6);
                triangle.Close();
                canvas.DrawPath(triangle, paint);
                break;
            }

            case PointSnapKind.Center:
                canvas.DrawCircle(x, y, h, paint);
                break;
            case PointSnapKind.GeometricCenter:
                canvas.DrawCircle(x, y, h, paint);
                canvas.DrawLine(x - h, y, x + h, y, paint);
                canvas.DrawLine(x, y - h, x, y + h, paint);
                break;
            case PointSnapKind.Node:
                canvas.DrawCircle(x, y, h - 1, paint);
                canvas.DrawLine(x - 3.5f, y - 3.5f, x + 3.5f, y + 3.5f, paint);
                canvas.DrawLine(x - 3.5f, y + 3.5f, x + 3.5f, y - 3.5f, paint);
                break;
            case PointSnapKind.Quadrant:
            {
                using var diamond = new SKPath();
                diamond.MoveTo(x, y - 7);
                diamond.LineTo(x + 7, y);
                diamond.LineTo(x, y + 7);
                diamond.LineTo(x - 7, y);
                diamond.Close();
                canvas.DrawPath(diamond, paint);
                break;
            }

            case PointSnapKind.Intersection:
                canvas.DrawLine(x - h, y - h, x + h, y + h, paint);
                canvas.DrawLine(x - h, y + h, x + h, y - h, paint);
                break;
            case PointSnapKind.Extension:
                canvas.DrawLine(x - 7, y, x - 4, y, paint);
                canvas.DrawLine(x - 1.5f, y, x + 1.5f, y, paint);
                canvas.DrawLine(x + 4, y, x + 7, y, paint);
                break;
            case PointSnapKind.Insertion:
                canvas.DrawRect(x - h, y - h, 2 * h, 2 * h, paint);
                canvas.DrawLine(x - h, y, x + h, y, paint);
                canvas.DrawLine(x, y - h, x, y + h, paint);
                break;
            case PointSnapKind.Perpendicular:
                canvas.DrawLine(x - h, y - h, x - h, y + h, paint);
                canvas.DrawLine(x - h, y + h, x + h, y + h, paint);
                canvas.DrawRect(x - h, y + h - 5, 5, 5, paint);
                break;
            case PointSnapKind.Tangent:
                canvas.DrawCircle(x, y - 1, 5, paint);
                canvas.DrawLine(x - 8, y + 4, x + 8, y + 4, paint);
                break;
            case PointSnapKind.Nearest:
            {
                using var hourglass = new SKPath();
                hourglass.MoveTo(x - h, y - h);
                hourglass.LineTo(x + h, y - h);
                hourglass.LineTo(x - h, y + h);
                hourglass.LineTo(x + h, y + h);
                hourglass.Close();
                canvas.DrawPath(hourglass, paint);
                break;
            }

            case PointSnapKind.ApparentIntersection:
                canvas.DrawLine(x - 4, y - 4, x + 4, y + 4, paint);
                canvas.DrawLine(x - 4, y + 4, x + 4, y - 4, paint);
                canvas.DrawRect(x - h - 1, y - h - 1, 2 * h + 2, 2 * h + 2, paint);
                break;
            default: // Parallel
                canvas.DrawLine(x - 7, y + 2, x + 1, y - 6, paint);
                canvas.DrawLine(x - 1, y + 6, x + 7, y - 2, paint);
                break;
        }
    }
}
