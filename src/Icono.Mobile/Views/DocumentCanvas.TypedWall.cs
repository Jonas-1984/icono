using Icono.Core;
using Icono.Editor;
using Icono.Rendering;

namespace Icono.Mobile.Views;

/// <summary>Giving the next wall (or piece of a polyline) a length while it is drawn: the number is typed, a direction is chosen (right, down, left,
/// up) and the corner is placed there; the next piece starts from it. The number is the length the wall's dimension shows (the inside length), and
/// a room that comes back to its first corner is closed - all of it is <see cref="WallDrawTyping"/>, shared with the Windows program.</summary>
public sealed partial class DocumentCanvas
{
    private WallDrawTyping? _wallTyping;

    /// <summary>The typing of lengths for the context of this canvas (made again when the context is replaced).</summary>
    private WallDrawTyping? WallTyping =>
        _context is null ? null : _wallTyping is { } state && ReferenceEquals(state.Context, _context) ? state : _wallTyping = new WallDrawTyping(_context);

    /// <summary>True while a wall or polyline has its first point: its next piece can be given a length.</summary>
    public bool CanTypeLength => _pendingPath is { Kind: ShapeKind.Wall or ShapeKind.Polyline, Points.Count: >= 2 } && !_placingFirstPoint;

    /// <summary>Places the next corner <paramref name="text"/> away from the last one, in the direction (<paramref name="dx"/>, <paramref name="dy"/>)
    /// (a unit step on the page: right = 1,0; down = 0,1). A room that comes back to its start is closed and the wall is finished.</summary>
    public TypedPlacement PlaceTypedLength(string? text, double dx, double dy)
    {
        if (!CanTypeLength || _pendingPath is not { } path || WallTyping is not { } typing || _context is not { } context)
        {
            return TypedPlacement.NotALength;
        }

        // the moving end is put in the chosen direction (the finger decides nothing here); the typed number decides how far
        var points = path.Points!.ToList();
        var from = points[^2];
        points[^1] = new CorePoint(from.X + dx * 100, from.Y + dy * 100);
        path.Points = points;
        PathBoundsCalculator.Recompute(path);

        typing.Clear();
        foreach (var c in (text ?? string.Empty).Where(c => !char.IsWhiteSpace(c)))
        {
            typing.Append(c, path);
        }

        var result = typing.Place(path);
        switch (result)
        {
            case TypedPlacement.NotALength:
                typing.Clear();
                points = path.Points!.ToList();
                points[^1] = points[^2]; // the moving end waits on the last corner again
                path.Points = points;
                PathBoundsCalculator.Recompute(path);
                context.Notify("That is not a length. Type for example 250, 2.5 m or 2500 mm.");
                context.RequestRedraw();
                break;
            case TypedPlacement.RoomClosed:
                FinishPath(closeWall: true);
                break;
            default:
                PathDrawingChanged?.Invoke(this, EventArgs.Empty);
                break;
        }

        return result;
    }
}
