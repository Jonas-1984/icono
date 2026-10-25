using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Which dimension of a door, window or opening was meant.</summary>
public enum SymbolDimensionPart
{
    /// <summary>The width of the opening, from jamb to jamb.</summary>
    Width,

    /// <summary>The clear distance from the corner at the start of the wall to the nearer jamb.</summary>
    GapToStart,

    /// <summary>The clear distance from the other jamb to the corner at the end of the wall.</summary>
    GapToEnd,
}

/// <summary>A dimension of a symbol as drawn, with what is needed to edit it.</summary>
public sealed record SymbolDimension(SymbolDimensionPart Part, DimensionLayout Layout, double Length);

/// <summary>The wall a symbol sits in: its segment and the inner face of that segment.</summary>
public sealed record SymbolHost(Shape Wall, int Segment, SKPoint FaceFrom, SKPoint FaceTo)
{
    public float FaceLength => Geo.Distance(FaceFrom, FaceTo);

    /// <summary>Unit vector along the face, from its start to its end.</summary>
    public SKPoint Direction
    {
        get
        {
            var length = Math.Max(1e-6f, FaceLength);
            return new SKPoint((FaceTo.X - FaceFrom.X) / length, (FaceTo.Y - FaceFrom.Y) / length);
        }
    }
}

/// <summary>
/// The distances that place a door or window exactly: the width of the opening and the clear distance from each of its jambs to the
/// corner of the wall (measured on the room side of the wall, like the inside dimensions of the wall itself). They are shown next to the
/// symbol and can be typed: the width resizes the opening, a distance moves it along the wall.
/// </summary>
public static class SymbolMeasurements
{
    /// <summary>The document being rendered (so a symbol can find its wall while it is drawn). Set by <see cref="DocumentRenderer"/>.</summary>
    [ThreadStatic]
    internal static Document? Current;

    /// <summary>All dimensions of the symbol that are shown.</summary>
    public static List<SymbolDimension> Layout(Document document, Shape symbol)
    {
        var result = new List<SymbolDimension>();
        if (!ArchitecturalSymbols.IsSymbol(symbol.Kind))
        {
            return result;
        }

        var host = FindHost(document, symbol);
        if (ArchitecturalSymbols.LayoutDimension(symbol, host is null ? 0f : ClearOfWallDimensions(symbol, host)) is { } width)
        {
            result.Add(new SymbolDimension(SymbolDimensionPart.Width, width, symbol.Width));
        }

        if (symbol.SymbolShowGaps && host is not null)
        {
            result.AddRange(Gaps(symbol, host));
        }

        return result;
    }

    /// <summary>The wall segment the symbol lies in, or null when it lies in no wall (it must run parallel to the wall and sit in its band).</summary>
    public static SymbolHost? FindHost(Document document, Shape symbol)
    {
        var (w, h) = ArchitecturalSymbols.FrameSize(symbol);
        var matrix = ArchitecturalSymbols.PlaceMatrix(symbol, w, h);
        var stripCentreY = ArchitecturalSymbols.IsDoor(symbol.Kind) ? h - (float)Math.Max(symbol.WallThickness, 0.5) / 2 : h / 2;
        var centre = matrix.MapPoint(new SKPoint(w / 2, stripCentreY));
        var axis = matrix.MapVector(1, 0);
        var axisLength = MathF.Sqrt(axis.X * axis.X + axis.Y * axis.Y);
        if (axisLength < 1e-6f)
        {
            return null;
        }

        axis = new SKPoint(axis.X / axisLength, axis.Y / axisLength);
        var roomLocal = matrix.MapVector(0, -1);

        SymbolHost? best = null;
        var bestDistance = float.MaxValue;
        foreach (var wall in document.VisibleShapes().Where(s => s.Kind == ShapeKind.Wall && s.Points is { Count: > 1 }))
        {
            var segments = WallGeometry.Segments(wall);
            for (var i = 0; i < segments.Count; i++)
            {
                var (from, to) = segments[i];
                var length = Geo.Distance(from, to);
                if (length < 1f)
                {
                    continue;
                }

                var dx = (to.X - from.X) / length;
                var dy = (to.Y - from.Y) / length;
                if (MathF.Abs(axis.X * dy - axis.Y * dx) > 0.05f)
                {
                    continue; // not parallel to the wall
                }

                var t = (centre.X - from.X) * dx + (centre.Y - from.Y) * dy;
                var across = MathF.Abs(-(centre.X - from.X) * dy + (centre.Y - from.Y) * dx);
                var reach = (float)wall.WallThickness / 2 + (float)symbol.WallThickness / 2 + 2f;
                if (t < -1f || t > length + 1f || across > reach || across >= bestDistance)
                {
                    continue;
                }

                // the room side: for a ring the inside, otherwise the side the symbol calls its inner one
                var leftNormal = new SKPoint(-dy, dx);
                float faceSide;
                if (wall.WallClosed)
                {
                    faceSide = -WallRenderer.DimensionSide(wall);
                }
                else
                {
                    faceSide = leftNormal.X * roomLocal.X + leftNormal.Y * roomLocal.Y >= 0 ? 1f : -1f;
                }

                var (fa, fb) = WallGeometry.FacePoints(wall, i, faceSide);
                best = new SymbolHost(wall, i, fa, fb);
                bestDistance = across;
            }
        }

        return best;
    }

    private static List<SymbolDimension> Gaps(Shape symbol, SymbolHost host)
    {
        var result = new List<SymbolDimension>();
        var (w, h) = ArchitecturalSymbols.FrameSize(symbol);
        var matrix = ArchitecturalSymbols.PlaceMatrix(symbol, w, h);
        var stripCentreY = ArchitecturalSymbols.IsDoor(symbol.Kind) ? h - (float)Math.Max(symbol.WallThickness, 0.5) / 2 : h / 2;
        var jamb0 = matrix.MapPoint(new SKPoint(0, stripCentreY));
        var jamb1 = matrix.MapPoint(new SKPoint(w, stripCentreY));

        var u = host.Direction;
        float T(SKPoint p) => (p.X - host.FaceFrom.X) * u.X + (p.Y - host.FaceFrom.Y) * u.Y;
        var near = Math.Min(T(jamb0), T(jamb1));
        var far = Math.Max(T(jamb0), T(jamb1));

        SKPoint OnFace(float t) => new(host.FaceFrom.X + u.X * t, host.FaceFrom.Y + u.Y * t);

        if (near > 0.5f)
        {
            result.Add(Gap(symbol, host, SymbolDimensionPart.GapToStart, OnFace(0), OnFace(near), near));
        }

        var rest = host.FaceLength - far;
        if (rest > 0.5f)
        {
            result.Add(Gap(symbol, host, SymbolDimensionPart.GapToEnd, OnFace(far), OnFace(host.FaceLength), rest));
        }

        return result;
    }

    private static SymbolDimension Gap(Shape symbol, SymbolHost host, SymbolDimensionPart part, SKPoint from, SKPoint to, float length)
    {
        var u = host.Direction;
        var side = SideOf(symbol, host);

        // the line sits on the side chosen for the width dimension; the measured face is the room side: across the wall when they differ
        var distance = (float)Math.Max(0, symbol.DimOffset)
            + (FaceSideOf(host) == side ? ClearOfWallDimensions(symbol, host) : (float)host.Wall.WallThickness);
        var along = (float)Math.Clamp(symbol.DimTextPosition ?? 0.5, 0, 1);
        var shift = (float)symbol.DimTextShift;
        var style = DimensionStyle.For(symbol);
        var text = DimensionRenderer.Format(length, symbol.DimUnit, symbol.DimPrecision);

        // LayoutAligned puts the line on the left of its first point to its second
        var layout = side > 0
            ? DimensionRenderer.LayoutAligned(from, to, distance, style, text, along, shift)
            : DimensionRenderer.LayoutAligned(to, from, distance, style, text, 1 - along, shift);
        return new SymbolDimension(part, layout, length);
    }

    /// <summary>Which side of the wall (+1 left of its direction, -1 right) the dimensions of the symbol are on.</summary>
    private static float SideOf(Shape symbol, SymbolHost host)
    {
        var u = host.Direction;
        var away = ArchitecturalSymbols.DimensionAway(symbol);
        return -u.Y * away.X + u.X * away.Y >= 0 ? 1f : -1f;
    }

    /// <summary>The extra distance that keeps the dimensions of the symbol off the wall's own dimension line when both are on the room side.</summary>
    private static float ClearOfWallDimensions(Shape symbol, SymbolHost host) =>
        host.Wall.WallShowDimensions && FaceSideOf(host) == SideOf(symbol, host) ? (float)host.Wall.DimOffset + 14f : 0f;

    private static float FaceSideOf(SymbolHost host)
    {
        // the face lies on the left of the wall's own direction when it is offset to the left of the centre line
        var segments = WallGeometry.Segments(host.Wall);
        var (from, to) = segments[host.Segment];
        var length = Geo.Distance(from, to);
        var nx = -(to.Y - from.Y) / length;
        var ny = (to.X - from.X) / length;
        var mid = new SKPoint((host.FaceFrom.X + host.FaceTo.X) / 2, (host.FaceFrom.Y + host.FaceTo.Y) / 2);
        return (mid.X - from.X) * nx + (mid.Y - from.Y) * ny >= 0 ? 1f : -1f;
    }

    /// <summary>Which dimension of the symbol has its number at the point (for clicking a number to type it), or null.</summary>
    public static SymbolDimension? HitTestText(Document document, Shape symbol, double x, double y, double tolerance)
    {
        foreach (var dimension in Layout(document, symbol))
        {
            var layout = dimension.Layout;
            if (layout.Text.Length == 0)
            {
                continue;
            }

            var radians = -layout.TextAngle * Math.PI / 180;
            var dx = x - layout.TextPosition.X;
            var dy = y - layout.TextPosition.Y;
            var localX = dx * Math.Cos(radians) - dy * Math.Sin(radians);
            var localY = dx * Math.Sin(radians) + dy * Math.Cos(radians);
            if (Math.Abs(localX) <= layout.TextWidth / 2 + tolerance && Math.Abs(localY) <= layout.FontSize * 0.7 + tolerance)
            {
                return dimension;
            }
        }

        return null;
    }

    /// <summary>Makes the opening <paramref name="width"/> wide, keeping the jamb at the start of the symbol where it is (the other one moves).</summary>
    public static bool SetOpeningWidth(Shape symbol, double width)
    {
        if (!ArchitecturalSymbols.IsSymbol(symbol.Kind) || width < 1 || Math.Abs(symbol.Width - width) < 1e-6)
        {
            return false;
        }

        SKPoint Jamb()
        {
            var (w, h) = ArchitecturalSymbols.FrameSize(symbol);
            var y = ArchitecturalSymbols.IsDoor(symbol.Kind) ? h - (float)Math.Max(symbol.WallThickness, 0.5) / 2 : h / 2;
            return ArchitecturalSymbols.PlaceMatrix(symbol, w, h).MapPoint(new SKPoint(0, y));
        }

        var before = Jamb();
        symbol.Width = width;
        ArchitecturalSymbols.Normalize(symbol);

        // turning and mirroring happen about the centre of the frame, which moved: put the jamb back where it was
        var after = Jamb();
        symbol.X += before.X - after.X;
        symbol.Y += before.Y - after.Y;
        return true;
    }

    /// <summary>Slides the symbol along its wall so the clear distance to the corner at one end becomes <paramref name="gap"/>.
    /// Returns false when the symbol is in no wall or nothing changes.</summary>
    public static bool SetGap(Document document, Shape symbol, SymbolDimensionPart part, double gap)
    {
        if (part == SymbolDimensionPart.Width || gap < 0 || FindHost(document, symbol) is not { } host)
        {
            return false;
        }

        var current = Gaps(symbol, host).FirstOrDefault(g => g.Part == part);
        if (current is null)
        {
            return false;
        }

        var delta = (float)(gap - current.Length);
        if (Math.Abs(delta) < 1e-4f)
        {
            return false;
        }

        // more distance to the start corner: further along the wall; more distance to the end corner: back along it
        var sign = part == SymbolDimensionPart.GapToStart ? 1f : -1f;
        var u = host.Direction;
        symbol.X += u.X * delta * sign;
        symbol.Y += u.Y * delta * sign;
        return true;
    }
}
