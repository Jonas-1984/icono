using Avalonia;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Mobile.Views;

/// <summary>The grid and the snapping of what is moved: the grid of the page (shown, and positions rounded to it), and the faces of walls and the
/// edges of other objects that a selection being moved lines up with - the settings are those of the <see cref="Icono.Editor.EditorContext"/>
/// (Properties of the drawing), the snappers are the ones of the Windows program.</summary>
public sealed partial class DocumentCanvas
{
    /// <summary>How near (DIP on the screen) an edge or a grid line has to be to take a moved object.</summary>
    private const double EdgeReach = 12;

    /// <summary>The lines the moved selection lined up on (shown while the finger moves it).</summary>
    private IReadOnlyList<ObjectSnapGuide> _objectGuides = [];

    private bool GridSnapping => _context is { SnapToGrid: true, GridSize: > 0 };

    /// <summary>The nearest grid line when snapping to the grid is on, else the value itself.</summary>
    private double GridValue(double value) =>
        _context is { SnapToGrid: true, GridSize: > 0 } context ? Math.Round(value / context.GridSize) * context.GridSize : value;

    private Point GridPoint(Point at) => GridSnapping ? new Point(GridValue(at.X), GridValue(at.Y)) : at;

    /// <summary>The rounding for the sizes of a resize (null when the grid does not snap).</summary>
    private Func<double, double>? GridRounding => GridSnapping ? GridValue : null;

    /// <summary>The moved selection snaps to the faces of walls and the edges of other objects as one frame (a group keeps its shape); where
    /// nothing is near, its first object's position goes onto the grid when that snaps. The lines it lined up on are remembered to show.</summary>
    private void SnapMovedSelection()
    {
        _objectGuides = [];
        if (_context is not { } context || _editShapes.Count == 0)
        {
            return;
        }

        // a corner, middle or centre of the moved objects on a point of another object comes first; else the edges and the grid
        if (SnapSelectionToPoints())
        {
            return;
        }

        var frame = Icono.Editor.ShapeResizer.GroupBounds(_editShapes);
        var result = ObjectSnapper.Snap(context.Document, frame, _editShapes, EdgeReach / _zoom, context.ObjectSnap);
        var dx = result.Dx;
        var dy = result.Dy;
        _objectGuides = result.Guides;

        // an axis that did not line up on an edge goes onto the grid instead
        if (GridSnapping)
        {
            var first = _editShapes[0];
            if (!result.Guides.Any(g => g.Vertical))
            {
                dx = GridValue(first.X) - first.X;
            }

            if (!result.Guides.Any(g => !g.Vertical))
            {
                dy = GridValue(first.Y) - first.Y;
            }
        }

        if (dx == 0 && dy == 0)
        {
            return;
        }

        foreach (var shape in _editShapes)
        {
            shape.X += dx;
            shape.Y += dy;
        }
    }

    /// <summary>The lines of the snap of the selection being moved, in screen units.</summary>
    private IReadOnlyList<(SKPoint From, SKPoint To)> ObjectGuidesForRender() =>
        _gesture == Gesture.MoveShapes
            ? _objectGuides
                .Select(g => g.Vertical
                    ? (ToScreen((g.Position, g.From)), ToScreen((g.Position, g.To)))
                    : (ToScreen((g.From, g.Position)), ToScreen((g.To, g.Position))))
                .ToList()
            : [];
}
