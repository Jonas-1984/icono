namespace Icono.Core;

/// <summary>Geometry helpers shared by the renderer and the editor for <see cref="ShapeKind.DesignGrid"/>.</summary>
public static class DesignGridMath
{
    public const double PxPerMm = 96.0 / 25.4;

    /// <summary>2 mm squares at the editor's 96 DPI.</summary>
    public const double DefaultCellSizePx = 2 * PxPerMm;

    /// <summary>Key of the square in row <paramref name="row"/>, column <paramref name="col"/>. Independent of the grid's
    /// current size, so growing or shrinking the grid never shifts already painted squares.</summary>
    public static int Key(int row, int col) => (row << 16) | (col & 0xFFFF);

    public static (int Row, int Col) FromKey(int key) => (key >> 16, key & 0xFFFF);

    public static int Columns(Shape shape) => Math.Max(1, (int)Math.Ceiling(shape.Width / shape.GridCellSize - 1e-6));

    public static int Rows(Shape shape) => Math.Max(1, (int)Math.Ceiling(shape.Height / shape.GridCellSize - 1e-6));

    /// <summary>Maps a point in document space onto the square of <paramref name="shape"/> under it, honouring the
    /// shape's rotation and flips. Returns false when the point lies outside the grid.</summary>
    public static bool TryGetCell(Shape shape, double docX, double docY, out int row, out int col)
    {
        row = col = 0;

        var cx = shape.X + shape.Width / 2;
        var cy = shape.Y + shape.Height / 2;
        var dx = docX - cx;
        var dy = docY - cy;

        // Undo the renderer's "rotate, then flip" about the centre.
        if (shape.Rotation != 0)
        {
            var rad = -shape.Rotation * Math.PI / 180.0;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);
            (dx, dy) = (dx * cos - dy * sin, dx * sin + dy * cos);
        }

        if (shape.FlipHorizontal)
        {
            dx = -dx;
        }

        if (shape.FlipVertical)
        {
            dy = -dy;
        }

        var localX = dx + shape.Width / 2;
        var localY = dy + shape.Height / 2;
        if (localX < 0 || localY < 0 || localX >= shape.Width || localY >= shape.Height)
        {
            return false;
        }

        col = (int)(localX / shape.GridCellSize);
        row = (int)(localY / shape.GridCellSize);
        return true;
    }
}
