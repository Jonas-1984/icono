using System.Windows;
using System.Windows.Input;
using Icono.App.State;
using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.App.Views;

/// <summary>Typing the length of a wall while it is drawn: the pointer gives the direction, the number gives the length, Enter places the corner
/// and the next wall starts there.</summary>
public partial class CanvasHostView
{
    /// <summary>What the user typed for the length of the wall being drawn (empty: the pointer decides).</summary>
    private string _wallTyped = string.Empty;

    /// <summary>The wall being drawn when at least one corner is placed (the last point is the moving one).</summary>
    private Shape? DrawingWallWithSegment =>
        _context is { ActiveTool: ActiveTool.PathDraw } && _isPathDrawing && _pendingPathShape is { Kind: ShapeKind.Wall, Points.Count: >= 2 } wall
            ? wall
            : null;

    private void ClearWallTyped() => _wallTyped = string.Empty;

    /// <summary>Key handling while a wall is drawn (called from the window): digits, a decimal point and unit letters fill the length box,
    /// Backspace edits it, Enter places the corner, Esc clears it. Returns true when the key was used.</summary>
    public bool HandleWallDrawKey(KeyEventArgs e)
    {
        if (DrawingWallWithSegment is null || Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            return false;
        }

        var typing = _wallTyped.Length > 0;
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        char? typed = key switch
        {
            >= Key.D0 and <= Key.D9 when !shift => (char)('0' + (key - Key.D0)),
            >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (key - Key.NumPad0)),
            Key.OemPeriod or Key.Decimal => '.',
            Key.OemComma => ',',
            >= Key.A and <= Key.Z when typing && !shift => (char)('a' + (key - Key.A)), // "m", "cm", "mm", "in" after the number
            _ => null,
        };

        if (typed is { } c)
        {
            _wallTyped += c;
            ApplyTypedLength();
            return true;
        }

        switch (key)
        {
            case Key.Back when typing:
                _wallTyped = _wallTyped[..^1];
                ApplyTypedLength();
                return true;
            case Key.Escape when typing:
                ClearWallTyped();
                _context?.RequestRedraw();
                return true;
            case Key.Enter when typing:
                PlaceTypedWallPoint();
                return true;
            default:
                return false;
        }
    }

    private bool TryTypedLength(Shape wall, out double px) =>
        WallGeometry.TryParseLength(_wallTyped, wall.WallDimensionUnit, _context?.Document.ScaleDenominator ?? 1, out px) && px > 0.5;

    /// <summary>The moving corner of the wall at the typed length from the last placed one, in the direction it has now.</summary>
    private Point WithTypedLength(Shape wall, Point point)
    {
        if (_wallTyped.Length == 0 || !TryTypedLength(wall, out var px))
        {
            return point;
        }

        var from = WallGeometry.AbsolutePoints(wall)[^2];
        var dx = point.X - from.X;
        var dy = point.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6)
        {
            // no direction yet: carry on in the direction of the previous wall, or to the right
            var points = WallGeometry.AbsolutePoints(wall);
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

        return new Point(from.X + dx / length * px, from.Y + dy / length * px);
    }

    private void ApplyTypedLength()
    {
        if (DrawingWallWithSegment is not { } wall)
        {
            return;
        }

        var points = WallGeometry.AbsolutePoints(wall);
        var moved = WithTypedLength(wall, new Point(points[^1].X, points[^1].Y));
        var list = wall.Points!.ToList();
        list[^1] = new CorePoint(moved.X - wall.X, moved.Y - wall.Y);
        wall.Points = list;
        PathBoundsCalculator.Recompute(wall);
        _context?.RequestRedraw();
    }

    /// <summary>Enter with a typed length: the corner is placed there and the next wall starts from it. When it lands on the first
    /// corner the room is closed.</summary>
    private void PlaceTypedWallPoint()
    {
        if (DrawingWallWithSegment is not { } wall || _context is null)
        {
            return;
        }

        if (!TryTypedLength(wall, out _))
        {
            _context.Notify("That is not a length. Type for example 250, 2.5 m or 2500 mm and press Enter.");
            return;
        }

        ApplyTypedLength();
        ClearWallTyped();

        var points = WallGeometry.AbsolutePoints(wall);
        var (sx, sy) = WallStart(wall);
        var closesRoom = points.Count - 1 >= 3 && Math.Abs(points[^1].X - sx) < 0.5 && Math.Abs(points[^1].Y - sy) < 0.5;
        if (closesRoom)
        {
            FinishPathDraw(closeWall: true);
            return;
        }

        var list = wall.Points!.ToList();
        list.Add(list[^1]);
        wall.Points = list;
        _context.RequestRedraw();
    }

    /// <summary>The small box next to the moving corner: asks for the length of the wall, and shows what is being typed.</summary>
    private void DrawWallLengthPrompt(SKCanvas canvas, float scaleX, float scaleY)
    {
        if (DrawingWallWithSegment is not { } wall || _context is null)
        {
            return;
        }

        var points = WallGeometry.AbsolutePoints(wall);
        var end = points[^1];
        var unit = wall.WallDimensionUnit;
        var text = _wallTyped.Length > 0
            ? $"Length  {_wallTyped}{(_wallTyped.Any(char.IsLetter) ? string.Empty : " " + UnitSuffix(unit))}|   Enter = place"
            : "Type a length, then Enter";
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
