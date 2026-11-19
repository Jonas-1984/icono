using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Icono.Editor;
using Icono.Rendering;

namespace Icono.Mobile.Views;

/// <summary>The grid and the snapping of the drawing (the Snap tab): the page grid, the points of other objects, the edges and faces a moved object lines
/// up with, and the helpers of the wall tool - the settings of the <see cref="EditorContext"/>, as in the Windows program. Every option is a small key
/// (with the symbol of the Windows program for the kinds of point) that is lit when it is on, so the lists stay short.</summary>
public sealed partial class PropertiesPanel
{
    /// <summary>Every kind of point of the Windows program's OBJECT SNAP card: its name, its symbol (14 x 14) and what it finds (shown when the key is held).</summary>
    private static readonly (PointSnapKind Kind, string Name, string Icon, string Tip)[] PointKinds =
    [
        (PointSnapKind.Endpoint, "Endpoint", "M2,2 H12 V12 H2 Z", "The corner or end of a line, curve, outline or wall."),
        (PointSnapKind.Midpoint, "Midpoint", "M7,2 L12.5,12 H1.5 Z", "The middle of a straight line or wall face."),
        (PointSnapKind.Center, "Center", "M7,1.5 A5.5,5.5 0 1 1 6.99,1.5 Z", "The centre of a circle or an ellipse."),
        (PointSnapKind.GeometricCenter, "Geometric center", "M7,1.5 A5.5,5.5 0 1 1 6.99,1.5 Z M1.5,7 H12.5 M7,1.5 V12.5", "The centre of area of a closed shape."),
        (PointSnapKind.Node, "Node", "M7,1.5 A5.5,5.5 0 1 1 6.99,1.5 Z M4.5,4.5 L9.5,9.5 M4.5,9.5 L9.5,4.5", "A definition point: the points of a dimension, the corners of a wall's centre line."),
        (PointSnapKind.Quadrant, "Quadrant", "M7,1 L13,7 L7,13 L1,7 Z", "The points of a circle or ellipse at 0, 90, 180 and 270 degrees."),
        (PointSnapKind.Intersection, "Intersection", "M2,2 L12,12 M2,12 L12,2", "Where two lines, curves or walls cross."),
        (PointSnapKind.Extension, "Extension", "M1,7 H3.5 M6,7 H8 M10.5,7 H13", "On the imagined continuation of an open line beyond its end (a dashed line shows it)."),
        (PointSnapKind.Insertion, "Insertion", "M2,2 H12 V12 H2 Z M2,7 H12 M7,2 V12", "The insertion point of a text, an image, a door, a window or a piece of furniture."),
        (PointSnapKind.Perpendicular, "Perpendicular", "M2,2 V12 H12 M2,8 H6 V12", "The foot of the line at a right angle from the last point to an object."),
        (PointSnapKind.Tangent, "Tangent", "M7,1.5 A3.5,3.5 0 1 1 6.99,1.5 Z M1,9.5 H13", "The point of a circle or curve where a line from the last point just touches it."),
        (PointSnapKind.Nearest, "Nearest", "M2,2 H12 L2,12 H12 Z", "The nearest point on an object (anywhere along its edge)."),
        (PointSnapKind.ApparentIntersection, "Apparent intersection", "M4,4 L10,10 M4,10 L10,4 M1.5,1.5 H12.5 V12.5 H1.5 Z", "Where the continuations of two lines meet although the lines themselves do not touch."),
        (PointSnapKind.Parallel, "Parallel", "M2,9 L8,3 M6,12 L12,6", "On the line from the last point that runs parallel to another line."),
    ];

    private void BuildSnapping(EditorContext context)
    {
        Heading("GRID");
        Chips(
            ("Show grid", null, "Show the grid on the page.", context.ShowGrid, on =>
            {
                context.ShowGrid = on;
                context.RequestRedraw();
            }),
            ("Snap to grid", null, "Points and moved objects jump to the grid.", context.SnapToGrid, on => context.SnapToGrid = on));
        RealRow("Square", context.GridSize, 5, 1, 1000, px =>
        {
            context.GridSize = Math.Max(1, px);
            context.RequestRedraw();
        });

        Heading("OBJECT SNAP");
        Chips(
            [("Object snap on", null, "The master switch (OSnap in the status line) of all snapping: points of other objects, the wall helpers, the first corner of a room, aligned edges and dimension lines, and the 15 degree steps of a rotation. Off: nothing is pulled; only the grid snaps, if Snap to grid is on.", context.PointSnap.Enabled,
                on => context.PointSnap.Enabled = on)]);
        Chips(PointKinds.Select(k =>
        {
            var kind = k.Kind;
            return (k.Name, (string?)k.Icon, k.Tip, context.PointSnap.IsOn(kind), (Action<bool>)(on => context.PointSnap.Set(kind, on)));
        }).ToArray());
        ButtonRow(
            ("All kinds", () => SetAllPointKinds(context, true)),
            ("No kind", () => SetAllPointKinds(context, false)));

        Heading("ALIGN WHILE MOVING");
        Chips(
            ("Align shapes", null, "Shapes line up with the faces of walls and the edges of other shapes while you move, place or resize them.", context.ObjectSnap.Enabled,
                on => context.ObjectSnap.Enabled = on),
            ("Wall faces", null, "To both faces and the corners of walls.", context.ObjectSnap.Walls, on => context.ObjectSnap.Walls = on),
            ("Other shapes", null, "To the edges and centres of other shapes.", context.ObjectSnap.Objects, on => context.ObjectSnap.Objects = on));

        Heading("WALL DRAWING");
        Chips(
            ("Helpers", null, "The helpers of the wall tool: the wall snaps to these.", context.WallSnap.Enabled, on => context.WallSnap.Enabled = on),
            ("Straight", null, "Across and up.", context.WallSnap.Axis, on => context.WallSnap.Axis = on),
            ("Parallel and right angle", null, "Parallel to another wall, or at a right angle to it.", context.WallSnap.Parallel, on => context.WallSnap.Parallel = on),
            ("Line up with corners", null, "In line with the corners of other walls.", context.WallSnap.Align, on => context.WallSnap.Align = on),
            ("Same length", null, "As long as the wall before.", context.WallSnap.EqualLength, on => context.WallSnap.EqualLength = on));
    }

    /// <summary>The options as small keys in two columns (with their symbol when they have one): lit when on, the explanation as a tool tip.</summary>
    private void Chips(params (string Label, string? Icon, string Tip, bool On, Action<bool> Set)[] items)
    {
        var grid = new UniformGrid { Columns = 2 };
        foreach (var (label, icon, tip, on, set) in items)
        {
            var current = on;
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions(icon is null ? "*" : "Auto,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var key = new Button
            {
                Classes = { "soft" }, MinHeight = 32, Margin = new Thickness(2), Padding = new Thickness(6, 3), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, Content = content,
            };
            if (icon is not null)
            {
                var symbol = new Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse(icon), StrokeThickness = 1.2, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Width = 14, Height = 14,
                    Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
                };
                symbol.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, key.GetObservable(TemplatedControl.ForegroundProperty));
                content.Children.Add(symbol);
            }

            var text = new TextBlock
            {
                Text = label, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(text, icon is null ? 0 : 1);
            content.Children.Add(text);
            key.Classes.Set("on", current);
            ToolTip.SetTip(key, tip);
            key.Click += (_, _) =>
            {
                // the key keeps its own state: a snap option is not part of the settings the panel is rebuilt from, so it is lit and unlit here
                current = !current;
                key.Classes.Set("on", current);
                set(current);
                _context?.NoteSettingsChanged(); // kept for the next start
            };
            grid.Children.Add(key);
        }

        _into.Children.Add(grid);
    }

    private void SetAllPointKinds(EditorContext context, bool on)
    {
        foreach (var (kind, _, _, _) in PointKinds)
        {
            context.PointSnap.Set(kind, on);
        }

        OnChanged();
    }
}
