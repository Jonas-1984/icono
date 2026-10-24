using Icono.Core;

namespace Icono.Rendering;

public enum AlignKind
{
    Left,
    CenterHorizontally,
    Right,
    Top,
    CenterVertically,
    Bottom,
}

/// <summary>Aligns and distributes shapes by their frames (X, Y, width, height). Only positions change: sizes and rotations stay.</summary>
public static class ShapeArranger
{
    /// <summary>Moves the shapes onto a common edge or centre line. With several shapes they align to the frame around all of them; a
    /// single shape aligns to the page. Returns the shapes that actually moved.</summary>
    public static List<Shape> Align(IReadOnlyList<Shape> shapes, AlignKind kind, double pageWidth, double pageHeight)
    {
        var moved = new List<Shape>();
        if (shapes.Count == 0)
        {
            return moved;
        }

        var left = shapes.Count == 1 ? 0 : shapes.Min(s => s.X);
        var top = shapes.Count == 1 ? 0 : shapes.Min(s => s.Y);
        var right = shapes.Count == 1 ? pageWidth : shapes.Max(s => s.X + s.Width);
        var bottom = shapes.Count == 1 ? pageHeight : shapes.Max(s => s.Y + s.Height);

        foreach (var shape in shapes)
        {
            var x = shape.X;
            var y = shape.Y;
            switch (kind)
            {
                case AlignKind.Left: x = left; break;
                case AlignKind.CenterHorizontally: x = (left + right) / 2 - shape.Width / 2; break;
                case AlignKind.Right: x = right - shape.Width; break;
                case AlignKind.Top: y = top; break;
                case AlignKind.CenterVertically: y = (top + bottom) / 2 - shape.Height / 2; break;
                default: y = bottom - shape.Height; break;
            }

            if (Math.Abs(x - shape.X) > 1e-9 || Math.Abs(y - shape.Y) > 1e-9)
            {
                shape.X = x;
                shape.Y = y;
                moved.Add(shape);
            }
        }

        return moved;
    }

    /// <summary>Spreads three or more shapes so the gaps between them are equal; the outermost two stay where they are.</summary>
    public static List<Shape> Distribute(IReadOnlyList<Shape> shapes, bool horizontally)
    {
        var moved = new List<Shape>();
        if (shapes.Count < 3)
        {
            return moved;
        }

        var ordered = (horizontally ? shapes.OrderBy(s => s.X) : shapes.OrderBy(s => s.Y)).ToList();
        double Start(Shape s) => horizontally ? s.X : s.Y;
        double Length(Shape s) => horizontally ? s.Width : s.Height;

        var first = Start(ordered[0]);
        var last = Start(ordered[^1]) + Length(ordered[^1]);
        var gap = (last - first - ordered.Sum(Length)) / (ordered.Count - 1);

        var position = first;
        foreach (var shape in ordered)
        {
            if (Math.Abs(position - Start(shape)) > 1e-9)
            {
                if (horizontally)
                {
                    shape.X = position;
                }
                else
                {
                    shape.Y = position;
                }

                moved.Add(shape);
            }

            position += Length(shape) + gap;
        }

        return moved;
    }
}
