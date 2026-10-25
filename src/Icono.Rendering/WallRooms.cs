using Icono.Core;

namespace Icono.Rendering;

/// <summary>Ready-made room outlines for the wall tool.</summary>
public enum RoomPreset
{
    /// <summary>A plain closed rectangle.</summary>
    Closed,

    /// <summary>An L-shaped room: one corner is cut out.</summary>
    OneCorner,

    /// <summary>A rectangle with one corner cut off by a slanted wall.</summary>
    OneSlantedWall,

    /// <summary>A rectangle with two corners cut off by slanted walls.</summary>
    TwoSlantedWalls,

    /// <summary>A column standing in a corner: that corner becomes a step, so one side has three corners.</summary>
    CornerColumn,

    /// <summary>A column standing against a wall, in the middle of it.</summary>
    WallColumn,
}

/// <summary>Builds the room presets as closed walls.</summary>
public static class WallRooms
{
    /// <summary>The corners of the room, clockwise from the top left, for a room of <paramref name="width"/> x <paramref name="height"/>
    /// (the first corner is at 0, 0).</summary>
    public static List<(double X, double Y)> Corners(RoomPreset preset, double width, double height)
    {
        // corners as fractions of the room's width and height
        (double X, double Y)[] f = preset switch
        {
            RoomPreset.OneCorner => [(0, 0), (1, 0), (1, 0.55), (0.55, 0.55), (0.55, 1), (0, 1)],
            RoomPreset.OneSlantedWall => [(0, 0), (0.68, 0), (1, 0.38), (1, 1), (0, 1)],
            RoomPreset.TwoSlantedWalls => [(0.25, 0), (0.75, 0), (1, 0.3), (1, 1), (0, 1), (0, 0.3)],
            RoomPreset.CornerColumn => [(0.15, 0), (1, 0), (1, 1), (0, 1), (0, 0.2), (0.15, 0.2)],
            RoomPreset.WallColumn => [(0, 0), (0.4, 0), (0.4, 0.14), (0.6, 0.14), (0.6, 0), (1, 0), (1, 1), (0, 1)],
            _ => [(0, 0), (1, 0), (1, 1), (0, 1)],
        };

        // the first corner of every preset but the plain ones is not at 0, 0: move the room so its frame starts there
        var minX = f.Min(p => p.X);
        var minY = f.Min(p => p.Y);
        return f.Select(p => ((p.X - minX) * width, (p.Y - minY) * height)).ToList();
    }

    /// <summary>A closed wall in the shape of the preset with its frame centred on (<paramref name="centerX"/>, <paramref name="centerY"/>).</summary>
    public static Shape Build(RoomPreset preset, double centerX, double centerY, double width, double height)
    {
        var corners = Corners(preset, width, height);
        var left = centerX - width / 2;
        var top = centerY - height / 2;

        var wall = new Shape
        {
            Kind = ShapeKind.Wall,
            X = left + corners[0].X,
            Y = top + corners[0].Y,
            WallClosed = true,
            Points = corners.Select(c => new CorePoint(c.X - corners[0].X, c.Y - corners[0].Y)).ToList(),
        };
        WallEditor.ApplyDrawingDefaults(wall);
        PathBoundsCalculator.Recompute(wall);
        return wall;
    }
}
