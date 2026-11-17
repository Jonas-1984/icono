using System.Windows;
using System.Windows.Input;
using Icono.App.State;
using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.App.Views;

/// <summary>Typing the length of a wall while it is drawn: the pointer gives the direction, the number gives the length, Enter places the corner
/// and the next wall starts there. What typing does is <see cref="WallDrawTyping"/> (shared with the tablet app); this part only reads the keys and
/// shows what is typed.</summary>
public partial class CanvasHostView
{
    private WallDrawTyping? _wallTypingState;

    /// <summary>The typing of lengths for the context of this canvas (made again when the context is replaced).</summary>
    private WallDrawTyping? WallTyping =>
        _context is null ? null : _wallTypingState is { } state && ReferenceEquals(state.Context, _context) ? state : _wallTypingState = new WallDrawTyping(_context);

    /// <summary>The wall, polyline or curve being drawn when at least one point is placed (the last point is the moving one). A dimension
    /// takes its length from the points it measures, so it is left out.</summary>
    private Shape? DrawingPathWithSegment =>
        _context is { ActiveTool: ActiveTool.PathDraw } && _isPathDrawing && _pendingPathShape is { Kind: not ShapeKind.Dimension, Points.Count: >= 2 } path
            ? path
            : null;

    private void ClearWallTyped() => WallTyping?.Clear();

    /// <summary>Key handling while a wall is drawn (called from the window): digits, a decimal point and unit letters fill the length box,
    /// Backspace edits it, Enter places the corner, Esc clears it. Returns true when the key was used.</summary>
    public bool HandleWallDrawKey(KeyEventArgs e)
    {
        if (DrawingPathWithSegment is not { } path || WallTyping is not { } typing
            || Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            return false;
        }

        var isTyping = typing.IsTyping;
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        char? typed = key switch
        {
            >= Key.D0 and <= Key.D9 when !shift => (char)('0' + (key - Key.D0)),
            >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (key - Key.NumPad0)),
            Key.OemPeriod or Key.Decimal => '.',
            Key.OemComma => ',',
            >= Key.A and <= Key.Z when isTyping && !shift => (char)('a' + (key - Key.A)), // "m", "cm", "mm", "in" after the number
            _ => null,
        };

        if (typed is { } c)
        {
            typing.Append(c, path);
            return true;
        }

        switch (key)
        {
            case Key.Back when isTyping:
                typing.Backspace(path);
                return true;
            case Key.Escape when isTyping:
                typing.Clear();
                _context?.RequestRedraw();
                return true;
            case Key.Enter when isTyping:
                PlaceTypedWallPoint();
                return true;
            default:
                return false;
        }
    }

    /// <summary>The moving corner of the wall: the typed length from the last placed one, in the direction the pointer gives (see
    /// <see cref="WallDrawTyping.WithTypedLength"/>). Without a typed length the point as it is.</summary>
    private Point WithTypedLength(Shape wall, Point point)
    {
        if (WallTyping is not { IsTyping: true } typing)
        {
            return point;
        }

        var moved = typing.WithTypedLength(wall, new SKPoint((float)point.X, (float)point.Y));
        return typing.TryLength(wall, out _) ? new Point(moved.X, moved.Y) : point;
    }

    /// <summary>A wall finished with a free end gives back what was added for a corner that never came.</summary>
    private void FitLastTypedSegment(Shape wall) => WallTyping?.FitLast(wall);

    private void ForgetTypedSegments() => WallTyping?.Forget();

    /// <summary>Enter with a typed length: the corner is placed there and the next wall starts from it. When it lands on the first
    /// corner the room is closed.</summary>
    private void PlaceTypedWallPoint()
    {
        if (DrawingPathWithSegment is not { } wall || _context is null || WallTyping is not { } typing)
        {
            return;
        }

        switch (typing.Place(wall))
        {
            case TypedPlacement.NotALength:
                _context.Notify("That is not a length. Type for example 250, 2.5 m or 2500 mm and press Enter.");
                break;
            case TypedPlacement.RoomClosed:
                FinishPathDraw(closeWall: true);
                break;
        }
    }

    /// <summary>The small box next to the moving corner: asks for the length of the wall, and shows what is being typed.</summary>
    private void DrawWallLengthPrompt(SKCanvas canvas, float scaleX, float scaleY)
    {
        if (DrawingPathWithSegment is not { } wall || _context is null || WallTyping is not { } typing)
        {
            return;
        }

        var points = WallGeometry.AbsolutePoints(wall);
        var end = points[^1];
        var unit = typing.UnitFor(wall);
        var text = typing.IsTyping
            ? $"Length  {typing.Typed}{(typing.Typed.Any(char.IsLetter) ? string.Empty : " " + UnitSuffix(unit))}|   Enter = place"
            : wall.Kind == ShapeKind.Wall
                ? "Type a length, then Enter"
                : _wallSnap is { Label.Length: > 0 }
                    ? "Type a length, then Enter" // the snap label already shows the length
                    : $"{DimensionRenderer.Format(Geo.Distance(points[^2], end), unit, 2, _context.Document.ScaleDenominator)}  -  type a length, then Enter";
        DrawPill(canvas, text, end.X * scaleX + 14, end.Y * scaleY + 6);
    }

    private static string UnitSuffix(LengthUnit unit) => unit switch
    {
        LengthUnit.Millimeter => "mm",
        LengthUnit.Meter => "m",
        LengthUnit.Inch => "in",
        _ => "cm",
    };
}
