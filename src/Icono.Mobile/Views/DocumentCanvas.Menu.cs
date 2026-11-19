using Avalonia.Controls;
using Avalonia.Interactivity;
using Icono.Core;
using Icono.Editor;
using Icono.Rendering;

namespace Icono.Mobile.Views;

/// <summary>The menu a long press opens - the tablet's right-click menu.</summary>
public sealed partial class DocumentCanvas
{
    /// <summary>The entries of the Windows right-click menu that depend on what is selected: combining, match properties, the corners of a wall, edit points,
    /// text to shape, the dimension of a door or window.</summary>
    private void AddMoreEntries(
        MenuFlyout flyout, Icono.Editor.EditorContext context, IReadOnlyList<Shape> selected, bool locked, Action<string, Action, bool> add, Action line)
    {
        var first = true;

        void Section()
        {
            if (first)
            {
                line();
                first = false;
            }
        }

        void Entry(string header, Action action, bool enabled = true)
        {
            Section();
            add(header, action, enabled);
        }

        if (selected.Count >= 2)
        {
            Entry("Union", context.UnionSelection, !locked);
            Entry("Trim (the first one cuts)", context.TrimSelection, !locked && selected.Count == 2);
            Entry("Merge to one shape", context.MergeSelection, !locked);
        }

        if (selected.Any(x => x.Kind is ShapeKind.CombinedPath or ShapeKind.Polyline))
        {
            Entry("Explode", context.ExplodeSelection, !locked);
        }

        Entry("Reflect", context.ReflectSelection, !locked);
        if (selected.Count == 1)
        {
            Entry("Match properties", context.BeginMatch);
        }

        if (selected.Count == 1 && selected[0] is { Kind: ShapeKind.Text } text && !locked)
        {
            Entry("Convert text to shape", () => context.ConvertTextToShape(text));
        }

        if (selected.Count == 1 && selected[0] is { Points: null } fixedShape && !locked && ShapeGeometryFactory.SupportsPointEditing(fixedShape.Kind))
        {
            Entry("Edit points", () => context.ConvertToEditablePoints(fixedShape));
        }

        if (selected.Count == 1 && ArchitecturalSymbols.IsSymbol(selected[0].Kind) && !locked)
        {
            var symbol = selected[0];
            Entry(symbol.SymbolShowDimension ? "Hide dimension" : "Show dimension", () => context.SetSymbolDimensions(!symbol.SymbolShowDimension, all: false));
        }

        if (selected.Count == 1 && selected[0] is { Kind: ShapeKind.Wall, Points.Count: >= 3 } wall && !locked)
        {
            // a corner of the wall under the finger: straighten it onto the line between its neighbours, or take it out
            var corner = NearestWallCorner(wall, ToDocument(_pressPoint));
            if (corner >= 0 && WallStraightener.Neighbours(wall, corner) is not null)
            {
                Entry("Straighten this corner", () => context.StraightenWallCorner(wall, corner));
                Entry("Remove this corner", () => PointEditing.RemovePointNear(context, wall, WallGeometry.AbsolutePoints(wall)[corner].X, WallGeometry.AbsolutePoints(wall)[corner].Y, 1),
                    wall.Points.Count > (wall.WallClosed ? 3 : 2));
            }

            Entry("Merge straight parts", context.MergeStraightWallParts);
            if (!wall.WallClosed)
            {
                Entry("Close room", context.CloseSelectedWalls);
            }
        }

        if (selected.Count >= 2 && selected.All(x => x.Kind == ShapeKind.Wall) && !locked)
        {
            Entry("Join walls into one", context.JoinSelectedWalls);
        }
    }

    /// <summary>The index of the corner of a wall that lies under the finger (within reach), or -1.</summary>
    private int NearestWallCorner(Shape wall, Avalonia.Point at)
    {
        var points = WallGeometry.AbsolutePoints(wall);
        var best = -1;
        var bestDistance = FingerReach * 1.4 / _zoom;
        for (var i = 0; i < points.Count; i++)
        {
            var distance = Math.Sqrt((points[i].X - at.X) * (points[i].X - at.X) + (points[i].Y - at.Y) * (points[i].Y - at.Y));
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private void ShowMenu(bool onObject)
    {
        if (_context is not { } context)
        {
            return;
        }

        var selected = context.SelectedShapes;
        var any = onObject && selected.Count > 0;
        var flyout = new MenuFlyout();

        void Add(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled, MinHeight = 36, FontSize = 14 };
            item.Click += (_, _) =>
            {
                action();
                context.RequestRedraw();
            };
            flyout.Items.Add(item);
        }

        void Line() => flyout.Items.Add(new Separator());

        if (any)
        {
            var locked = selected.All(s => s.Locked);
            if (selected.Count == 1 && CanEditText(selected[0]))
            {
                var shape = selected[0];
                Add("Edit text", () => RequestText(shape, LayerOf(shape), shape.X, shape.Y), !locked);
                Line();
            }

            Add("Cut", context.CutSelection, !locked);
            Add("Copy", context.CopySelection);
            Add("Paste", context.PasteClipboard, context.Clipboard.Count > 0);
            Add("Duplicate", context.DuplicateSelection);
            Add("Delete", context.DeleteSelection, !locked);
            Line();
            var grouped = selected.Count > 1 && selected.All(s => s.GroupId is { } id && id == selected[0].GroupId);
            if (selected.Any(s => s.GroupId is not null))
            {
                Add("Ungroup", context.UngroupSelection);
            }

            if (selected.Count > 1 && !grouped)
            {
                Add("Group", context.GroupSelection);
            }

            Add(locked ? "Unlock" : "Lock", () => context.SetSelectionLocked(!locked));
            Line();
            Add("Bring to front", context.BringSelectionToFront, !locked);
            Add("Send to back", context.SendSelectionToBack, !locked);
            AddMoreEntries(flyout, context, selected, locked, Add, Line);
        }
        else
        {
            Add("Paste", context.PasteClipboard, context.Clipboard.Count > 0);
            Add("Select all", context.SelectAll);
            Line();
            Add("Fit page", FitToView);
        }

        _menu = flyout;
        flyout.ShowAt(this, showAtPointer: true);
    }

    private MenuFlyout? _menu;

    private void CloseMenu()
    {
        _menu?.Hide();
        _menu = null;
    }
}
