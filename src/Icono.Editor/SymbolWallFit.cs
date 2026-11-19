using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Editor;

/// <summary>A door, window or wall opening that is put on a wall sits in it: it turns to the direction of that wall, takes its thickness and is centred on its
/// centre line, and a door swings into the room (a closed wall) or to the side that was tapped (an open wall). Placed away from every wall, a symbol stays as it is.</summary>
public static class SymbolWallFit
{
    /// <summary>The wall nearest to the place (within half its thickness plus <paramref name="reach"/>), the place on its centre line and its direction.</summary>
    public readonly record struct Hit(Shape Wall, WallEditor.Spot Spot, double DirectionX, double DirectionY, double SegmentLength);

    /// <summary>The wall under (<paramref name="x"/>, <paramref name="y"/>), or null. Hidden layers and locked walls are left out.</summary>
    public static Hit? FindWall(Document document, double x, double y, double reach)
    {
        Hit? best = null;
        var bestMargin = double.MaxValue;
        foreach (var layer in document.Layers)
        {
            if (!layer.Visible)
            {
                continue;
            }

            foreach (var wall in layer.Shapes)
            {
                if (wall.Kind != ShapeKind.Wall || wall.Locked || WallEditor.Nearest(wall, x, y, 0) is not { } spot)
                {
                    continue;
                }

                var margin = spot.Distance - wall.WallThickness / 2;
                if (margin > reach || margin >= bestMargin)
                {
                    continue;
                }

                var points = WallGeometry.AbsolutePoints(wall);
                var a = points[spot.Segment];
                var b = points[(spot.Segment + 1) % points.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 1e-6)
                {
                    continue;
                }

                bestMargin = margin;
                best = new Hit(wall, spot, dx / length, dy / length, length);
            }
        }

        return best;
    }

    /// <summary>Puts the symbol into the wall under <paramref name="x"/>, <paramref name="y"/> (a tap, or the middle of what was dragged). With
    /// <paramref name="width"/> the opening has that width, else the width the symbol has now. False when no wall is there: the symbol is left alone.</summary>
    public static bool Fit(Document document, Shape symbol, double x, double y, double reach, double? width = null)
    {
        if (!ArchitecturalSymbols.IsSymbol(symbol.Kind) || FindWall(document, x, y, reach) is not { } hit)
        {
            return false;
        }

        var wall = hit.Wall;
        var thickness = Math.Max(0.5, wall.WallThickness);
        var openingWidth = Math.Max(1, width ?? symbol.Width);

        // where on the centre line: the tapped place, kept inside the side of the wall when the opening fits in it
        var along = hit.Spot.T * hit.SegmentLength;
        if (hit.SegmentLength > openingWidth)
        {
            along = Math.Clamp(along, openingWidth / 2, hit.SegmentLength - openingWidth / 2);
        }

        var points = WallGeometry.AbsolutePoints(wall);
        var start = points[hit.Spot.Segment];
        var (px, py) = (start.X + hit.DirectionX * along, start.Y + hit.DirectionY * along);

        // the side to swing to: the room for a closed wall, else the side the tap was on
        var (nx, ny) = (hit.DirectionY, -hit.DirectionX); // the left of the way along the wall as seen on the screen (y down)
        var towardsRoom = RoomSide(points, wall.WallClosed, px, py, nx, ny, thickness) ?? ((x - px) * nx + (y - py) * ny >= 0 ? 1 : -1);
        var directionDegrees = Math.Atan2(hit.DirectionY, hit.DirectionX) * 180 / Math.PI;
        var rotation = towardsRoom > 0 ? directionDegrees : directionDegrees + 180;

        symbol.WallThickness = thickness;
        symbol.Width = openingWidth;
        symbol.Height = ArchitecturalSymbols.IsDoor(symbol.Kind) ? ArchitecturalSymbols.DoorHeight(symbol) : thickness;
        symbol.Rotation = rotation;

        // the wall strip of a door is the bottom of its frame, the swing goes to the top: the centre of the frame is half the swing away from the wall
        var offset = (symbol.Height - thickness) / 2;
        var swingX = towardsRoom > 0 ? nx : -nx;
        var swingY = towardsRoom > 0 ? ny : -ny;
        var (cx, cy) = (px + swingX * offset, py + swingY * offset);
        symbol.X = cx - symbol.Width / 2;
        symbol.Y = cy - symbol.Height / 2;
        return true;
    }

    /// <summary>+1 when the room of a closed wall lies on the left (<paramref name="nx"/>, <paramref name="ny"/>) of the wall at the place, -1 when it lies on the
    /// right, null when the wall is not closed (or the place is no help).</summary>
    private static int? RoomSide(IReadOnlyList<SKPoint> points, bool closed, double px, double py, double nx, double ny, double thickness)
    {
        if (!closed || points.Count < 3)
        {
            return null;
        }

        var probe = thickness / 2 + 4;
        var left = Contains(points, px + nx * probe, py + ny * probe);
        var right = Contains(points, px - nx * probe, py - ny * probe);
        return left == right ? null : left ? 1 : -1;
    }

    private static bool Contains(IReadOnlyList<SKPoint> polygon, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var (a, b) = (polygon[i], polygon[j]);
            if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
