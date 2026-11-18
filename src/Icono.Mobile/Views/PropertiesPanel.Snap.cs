using Icono.Editor;
using Icono.Rendering;

namespace Icono.Mobile.Views;

/// <summary>The grid and the snapping of the drawing (shown while nothing is selected): the page grid, the points of other objects, the edges and
/// faces a moved object lines up with, and the helpers of the wall tool - the settings of the <see cref="EditorContext"/>, as in the Windows program.</summary>
public sealed partial class PropertiesPanel
{
    private static readonly (PointSnapKind Kind, string Name)[] PointKinds =
    [
        (PointSnapKind.Endpoint, "Corners and ends"),
        (PointSnapKind.Midpoint, "Middles"),
        (PointSnapKind.Center, "Centres"),
        (PointSnapKind.Intersection, "Crossings"),
        (PointSnapKind.Perpendicular, "Right angles"),
        (PointSnapKind.Extension, "Extensions"),
        (PointSnapKind.Nearest, "Nearest point"),
    ];

    private void BuildSnapping(EditorContext context)
    {
        Heading("GRID");
        Toggle("Show grid", context.ShowGrid, on =>
        {
            context.ShowGrid = on;
            context.RequestRedraw();
        });
        Toggle("Snap to grid", context.SnapToGrid, on => context.SnapToGrid = on);
        RealRow("Square", context.GridSize, 5, 1, 1000, px =>
        {
            context.GridSize = Math.Max(1, px);
            context.RequestRedraw();
        });
        Note("Real size on the drawing (the scale is applied).");

        Heading("SNAP TO OBJECTS");
        Toggle("Points of objects", context.PointSnap.Enabled, on => context.PointSnap.Enabled = on);
        foreach (var (kind, name) in PointKinds)
        {
            var captured = kind;
            Toggle("   " + name, context.PointSnap.IsOn(captured), on => context.PointSnap.Set(captured, on));
        }

        Heading("WHEN MOVING");
        Toggle("Edges and faces", context.ObjectSnap.Enabled, on => context.ObjectSnap.Enabled = on);
        Toggle("   Faces of walls", context.ObjectSnap.Walls, on => context.ObjectSnap.Walls = on);
        Toggle("   Edges of objects", context.ObjectSnap.Objects, on => context.ObjectSnap.Objects = on);

        Heading("WALL DRAWING");
        Toggle("Helpers", context.WallSnap.Enabled, on => context.WallSnap.Enabled = on);
        Toggle("   Straight (across / up)", context.WallSnap.Axis, on => context.WallSnap.Axis = on);
        Toggle("   Parallel and right angle", context.WallSnap.Parallel, on => context.WallSnap.Parallel = on);
        Toggle("   Line up with corners", context.WallSnap.Align, on => context.WallSnap.Align = on);
        Toggle("   Same length", context.WallSnap.EqualLength, on => context.WallSnap.EqualLength = on);
    }
}
