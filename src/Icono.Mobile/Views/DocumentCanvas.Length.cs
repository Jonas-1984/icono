using Avalonia;
using Icono.Core;
using Icono.Editor;
using Icono.Rendering;

namespace Icono.Mobile.Views;

/// <summary>What to type: the title, a hint, the text to start with and what OK does with the typed text.</summary>
public sealed record LengthRequest(string Title, string Note, string Text, Action<string?> Commit);

/// <summary>Tap the number of a wall's dimension (or of a door / window) and type the length you want - the wall is made that long (see
/// <see cref="LengthEditing"/>, shared with the Windows program). A tap on the number wins over a tap on the wall below it.</summary>
public sealed partial class DocumentCanvas
{
    /// <summary>How far (DIP) from a dimension number a finger still takes it.</summary>
    private const double NumberReach = 10;

    /// <summary>The dimension number under the finger when it went down (wall segment or door / window), or null.</summary>
    private (Shape Shape, Layer Layer, int Segment, SymbolDimension? Symbol)? _pressNumber;

    /// <summary>Raised when the user tapped a dimension number: the window asks for the length.</summary>
    public event EventHandler<LengthRequest>? LengthRequested;

    /// <summary>The dimension number at a point of the drawing (walls first, then doors and windows), or null. Hidden and locked layers and
    /// locked objects are skipped.</summary>
    private (Shape Shape, Layer Layer, int Segment, SymbolDimension? Symbol)? FindDimensionNumber(Point at)
    {
        if (_context is not { } context)
        {
            return null;
        }

        var reach = NumberReach / _zoom;
        foreach (var layer in context.Document.Layers.Where(l => l.Visible && !l.Locked))
        {
            foreach (var wall in layer.Shapes.Where(s => s.Kind == ShapeKind.Wall && !s.Locked))
            {
                if (WallRenderer.HitTestDimensionText(wall, at.X, at.Y, reach) is { } segment)
                {
                    return (wall, layer, segment, null);
                }
            }
        }

        foreach (var layer in context.Document.Layers.Where(l => l.Visible && !l.Locked))
        {
            foreach (var symbol in layer.Shapes.Where(s => ArchitecturalSymbols.IsSymbol(s.Kind) && !s.Locked))
            {
                if (SymbolMeasurements.HitTestText(context.Document, symbol, at.X, at.Y, reach) is { } dimension)
                {
                    return (symbol, layer, -1, dimension);
                }
            }
        }

        return null;
    }

    /// <summary>The finger lifted on a dimension number: select its object and ask for the length.</summary>
    private void TapNumber((Shape Shape, Layer Layer, int Segment, SymbolDimension? Symbol) number)
    {
        if (_context is not { } context)
        {
            return;
        }

        Select(number.Shape, number.Layer);
        context.RequestRedraw();

        if (number.Symbol is { } dimension)
        {
            var symbol = number.Shape;
            var part = dimension.Part;
            LengthRequested?.Invoke(this, new LengthRequest(
                LengthEditing.SymbolTitle(part), LengthEditing.SymbolNote(part), LengthEditing.SymbolText(symbol, dimension),
                text => LengthEditing.CommitSymbol(context, symbol, part, text)));
            return;
        }

        var wall = number.Shape;
        var segment = number.Segment;
        context.PickedDimensionSegment = segment; // the DIMENSION settings of the wall show this one
        LengthRequested?.Invoke(this, new LengthRequest(
            "Wall length", LengthEditing.WallNote, LengthEditing.WallText(wall, segment),
            text => LengthEditing.CommitWall(context, wall, segment, text)));
    }
}
