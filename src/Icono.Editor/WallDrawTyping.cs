using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Editor;

/// <summary>What <see cref="WallDrawTyping.Place"/> did.</summary>
public enum TypedPlacement
{
    /// <summary>The typed text is no length: nothing happened (the caller tells the user).</summary>
    NotALength,

    /// <summary>The corner is placed and the next wall starts there.</summary>
    Placed,

    /// <summary>The corner landed on the first corner of the room: the room is closed and the drawing is to be finished as a closed wall.</summary>
    RoomClosed,
}

/// <summary>Typing the length of a wall while it is drawn - shared by the Windows program and the tablet app: the pointer (or finger) gives the
/// direction, the typed number gives the length, "Place" puts the corner there and the next wall starts from it. The number typed is the length the
/// dimension of the wall shows (see <see cref="WallTypedLength"/>): the centre line is made as long as needed, when the next wall is drawn the corner
/// moves so that this wall keeps the number that was typed, a free end gives the extra back at the finish, and a typed room that ends close to its start
/// is closed there. A polyline or curve takes the number as the length of the piece itself.</summary>
public sealed class WallDrawTyping(EditorContext context)
{
    /// <summary>The wall whose typed lengths are remembered, and per segment the length the user typed (page px).</summary>
    private Shape? _typedWall;

    private readonly Dictionary<int, double> _typedSegments = [];

    public EditorContext Context { get; } = context;

    /// <summary>What the user typed for the length of the wall being drawn (empty: the pointer decides).</summary>
    public string Typed { get; private set; } = string.Empty;

    public bool IsTyping => Typed.Length > 0;

    /// <summary>Forgets what is being typed (not the lengths of the walls already placed).</summary>
    public void Clear() => Typed = string.Empty;

    /// <summary>Forgets the typed lengths of the walls placed so far (the drawing ended or was cancelled).</summary>
    public void Forget()
    {
        _typedWall = null;
        _typedSegments.Clear();
    }

    /// <summary>The unit a bare number is in: the wall's own dimension unit, else the unit of the dimensions.</summary>
    public LengthUnit UnitFor(Shape path) => path.Kind == ShapeKind.Wall ? path.WallDimensionUnit : Context.DimensionUnit;

    /// <summary>The typed text as a length on the page.</summary>
    public bool TryLength(Shape path, out double px) =>
        WallGeometry.TryParseLength(Typed, UnitFor(path), Context.Document.ScaleDenominator, out px) && px > 0.5;

    // ---------------------------------------------------------------- typing

    /// <summary>A character typed: a digit, a decimal point, or (after a number) the letters of a unit. The moving end of <paramref name="path"/> follows.</summary>
    public void Append(char c, Shape path)
    {
        Typed += c;
        Apply(path);
    }

    /// <summary>The whole number is there at once (typed in a box, with the direction chosen by a key): the moving end of <paramref name="path"/> goes to its place in ONE step.
    /// Typing it character by character would work out the direction again after each character from the end it has just been put on, and the corner before it
    /// moves a little with every step (the wall before keeps its typed number) - a short first digit then turned the direction round.</summary>
    public void SetTyped(string? text, Shape path)
    {
        Typed = string.Concat((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c)));
        Apply(path);
    }

    /// <summary>Takes back the last character; false when nothing was typed.</summary>
    public bool Backspace(Shape path)
    {
        if (!IsTyping)
        {
            return false;
        }

        Typed = Typed[..^1];
        Apply(path);
        return true;
    }

    /// <summary>The moving end of the wall (the last of its points) goes where the typed length puts it.</summary>
    public void Apply(Shape path)
    {
        if (path.Points is not { Count: >= 2 })
        {
            return;
        }

        var points = WallGeometry.AbsolutePoints(path);
        var moved = WithTypedLength(path, points[^1]);

        // the points are read AFTER the call: it may have moved the corner before (the previous wall keeps its typed number), and that must stay
        var list = path.Points!.ToList();
        list[^1] = new CorePoint(moved.X - path.X, moved.Y - path.Y);
        path.Points = list;
        PathBoundsCalculator.Recompute(path);
        Context.RequestRedraw();
    }

    /// <summary>The moving corner of the wall: the typed length from the last placed one, in the direction the pointer gives (<paramref name="point"/>),
    /// with the centre line long enough that the dimension of this wall shows the typed number. Without a typed length: <paramref name="point"/>.</summary>
    public SKPoint WithTypedLength(Shape wall, SKPoint point)
    {
        if (!IsTyping || !TryLength(wall, out var px))
        {
            return point;
        }

        var points = WallGeometry.AbsolutePoints(wall).ToList();
        var from = points[^2];
        double dx = point.X - from.X;
        double dy = point.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6)
        {
            // no direction yet: carry on in the direction of the previous wall, or to the right
            if (points.Count >= 3)
            {
                dx = points[^2].X - points[^3].X;
                dy = points[^2].Y - points[^3].Y;
                length = Math.Sqrt(dx * dx + dy * dy);
            }

            if (length < 1e-6)
            {
                dx = 1;
                dy = 0;
                length = 1;
            }
        }

        var ux = dx / length;
        var uy = dy / length;
        if (wall.Kind != ShapeKind.Wall)
        {
            // a polyline or curve: the number is the length of the piece itself
            return new SKPoint((float)(from.X + ux * px), (float)(from.Y + uy * px));
        }

        if (TryTypedSegment(wall, points.Count - 3, out var previous))
        {
            WallTypedLength.KeepPreviousTyped(wall, points, previous, ux, uy);
            SetAbsolutePoints(wall, points);
            points = WallGeometry.AbsolutePoints(wall).ToList();
        }

        return WallTypedLength.EndFor(wall, points, ux, uy, px);
    }

    // ---------------------------------------------------------------- placing

    /// <summary>"Place" (Enter): the corner is placed at the typed length and the next wall starts from it. When it lands on the first corner of the
    /// room the room is closed (<see cref="TypedPlacement.RoomClosed"/>: the caller finishes the drawing as a closed wall).</summary>
    public TypedPlacement Place(Shape path)
    {
        if (!TryLength(path, out var typedPx))
        {
            return TypedPlacement.NotALength;
        }

        Apply(path);
        Clear();
        if (path.Kind != ShapeKind.Wall)
        {
            AddMovingPoint(path);
            return TypedPlacement.Placed;
        }

        Remember(path, path.Points!.Count - 2, typedPx);

        var points = WallGeometry.AbsolutePoints(path);
        var start = points[0];
        var gap = Math.Sqrt((points[^1].X - start.X) * (points[^1].X - start.X) + (points[^1].Y - start.Y) * (points[^1].Y - start.Y));
        if (points.Count - 1 >= 3 && gap <= path.WallThickness * 1.3 && TryCloseRoom(path, typedPx))
        {
            return TypedPlacement.RoomClosed;
        }

        AddMovingPoint(path);
        return TypedPlacement.Placed;
    }

    private void AddMovingPoint(Shape path)
    {
        var list = path.Points!.ToList();
        list.Add(list[^1]);
        path.Points = list;
        Context.RequestRedraw();
    }

    /// <summary>A wall finished with a free end gives back what was added for a corner that never came (call it when the drawing ends).</summary>
    public void FitLast(Shape wall)
    {
        if (wall.Points is { Count: >= 2 } local && TryTypedSegment(wall, local.Count - 2, out var target))
        {
            var points = WallGeometry.AbsolutePoints(wall).ToList();
            WallTypedLength.FitLast(wall, points, target);
            SetAbsolutePoints(wall, points);
        }

        Forget();
    }

    /// <summary>The typed wall ends close to the start of the room: the room is closed there (see <see cref="WallTypedLength.CloseRoom"/>).</summary>
    private static bool TryCloseRoom(Shape wall, double typedLast)
    {
        var points = WallGeometry.AbsolutePoints(wall).ToList();
        if (points.Count - 1 < 3)
        {
            return false;
        }

        var start = WallTypedLength.CloseRoom(wall, points, typedLast);
        points[0] = start;
        points[^1] = start;
        SetAbsolutePoints(wall, points);
        return true;
    }

    // ---------------------------------------------------------------- the lengths typed so far

    private void Remember(Shape wall, int segment, double px)
    {
        if (!ReferenceEquals(_typedWall, wall))
        {
            _typedWall = wall;
            _typedSegments.Clear();
        }

        _typedSegments[segment] = px;
    }

    private bool TryTypedSegment(Shape wall, int segment, out double px)
    {
        px = 0;
        return ReferenceEquals(_typedWall, wall) && _typedSegments.TryGetValue(segment, out px);
    }

    private static void SetAbsolutePoints(Shape wall, IReadOnlyList<SKPoint> absolute)
    {
        wall.Points = absolute.Select(p => new CorePoint(p.X - wall.X, p.Y - wall.Y)).ToList();
        PathBoundsCalculator.Recompute(wall);
    }
}
