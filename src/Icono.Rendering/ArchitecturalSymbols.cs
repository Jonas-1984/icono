using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>
/// Doors, windows and wall openings as they are drawn in an architectural plan. A symbol lies over a wall and cuts it open: a white
/// (fill-coloured) band hides the wall where the opening is, jamb lines mark its ends, and a door adds its leaf and the swing arc.
/// The symbol follows its frame (X, Y, Width, Height) like every other shape: a door is as wide as its opening and its leaf is exactly as long
/// as the opening is wide (the swing is a true circle), so the height of a door follows from its width: the strip at the bottom is the wall
/// (<see cref="Shape.WallThickness"/> thick) and above it is the room the leaf swings in. A window or an opening is only that strip: its
/// height is the wall thickness. Flipping mirrors the hinge side and the swing direction, rotating turns
/// it onto any wall.
/// </summary>
public static class ArchitecturalSymbols
{
    public static bool IsSymbol(ShapeKind kind) =>
        kind is ShapeKind.DoorSingle or ShapeKind.DoorDouble or ShapeKind.WindowSingle or ShapeKind.WindowDouble or ShapeKind.WallOpening;

    public static bool IsDoor(ShapeKind kind) => kind is ShapeKind.DoorSingle or ShapeKind.DoorDouble;

    public static bool IsWindow(ShapeKind kind) => kind is ShapeKind.WindowSingle or ShapeKind.WindowDouble;

    /// <summary>The pieces of a symbol in document space, ready to draw.</summary>
    public sealed class Parts : IDisposable
    {
        /// <summary>The area that cuts the wall open (filled with the symbol's fill colour).</summary>
        public SKPath Mask { get; } = new();

        /// <summary>Closed pieces drawn with fill and outline: the leaves of a door.</summary>
        public SKPath Leaves { get; } = new();

        /// <summary>Lines drawn with the outline colour: jambs, glass, frames and swing arcs.</summary>
        public SKPath Lines { get; } = new();

        public void Dispose()
        {
            Mask.Dispose();
            Leaves.Dispose();
            Lines.Dispose();
        }
    }

    /// <summary>What a newly placed symbol starts with: a light fill and a dark thin outline, a door open at a right angle.</summary>
    public static void ApplyDefaults(Shape shape)
    {
        shape.FillColor = ArgbColor.FromRgb(0xFF, 0xFF, 0xFF);
        shape.BorderColor = ArgbColor.FromRgb(0x23, 0x29, 0x33);
        shape.BorderThickness = 1.2;
        shape.DoorOpenAngle = 90;
        shape.DimOffset = 18;
        shape.SymbolShowDimension = true;
    }

    /// <summary>The size a symbol gets when it is placed with a click: a real-world width (door 90 cm, double door 160 cm, window 100 cm,
    /// double window 180 cm, opening 100 cm) at the drawing scale, never more than 45% of the page width.</summary>
    public static (double Width, double Height) DefaultSize(ShapeKind kind, double wallThickness, double scaleDenominator, double pageWidth)
    {
        var centimetres = kind switch
        {
            ShapeKind.DoorSingle => 90.0,
            ShapeKind.DoorDouble => 160.0,
            ShapeKind.WindowDouble => 180.0,
            ShapeKind.WallOpening => 100.0,
            _ => 100.0,
        };

        var width = centimetres / 2.54 * 96 / Math.Max(1, scaleDenominator);
        width = Math.Clamp(width, 24, Math.Max(24, pageWidth * 0.45));

        return kind switch
        {
            ShapeKind.DoorSingle => (width, wallThickness + width),
            ShapeKind.DoorDouble => (width, wallThickness + width / 2),
            _ => (width, wallThickness),
        };
    }

    /// <summary>The radius of the swing of a door: the length of its leaf. A single door swings its full width, each leaf of a double door half of it.</summary>
    public static double LeafLength(Shape door) => Math.Max(1, door.Width) / (door.Kind == ShapeKind.DoorDouble ? 2 : 1);

    /// <summary>The height a door has to have for its width: the wall strip plus the room the leaf swings in. Not the height of a window or an
    /// opening, which is the wall thickness and can be chosen freely.</summary>
    public static double DoorHeight(Shape door) => Math.Max(0.5, door.WallThickness) + LeafLength(door);

    /// <summary>Gives a door the height that goes with its width (see <see cref="DoorHeight"/>). Returns true when it changed.</summary>
    public static bool Normalize(Shape shape)
    {
        if (!IsDoor(shape.Kind))
        {
            return false;
        }

        var height = DoorHeight(shape);
        if (Math.Abs(shape.Height - height) < 0.01)
        {
            return false;
        }

        shape.Height = height;
        return true;
    }

    /// <summary>Builds the pieces of the symbol where it is drawn (flipped, placed and rotated).</summary>
    public static Parts Build(Shape shape)
    {
        var parts = new Parts();
        var w = (float)Math.Max(1, shape.Width);
        // a door is as high as its width needs, whatever height the frame has right now (a symbol being dragged out, or not yet fixed up)
        var h = (float)(IsDoor(shape.Kind) ? DoorHeight(shape) : Math.Max(1, shape.Height));

        switch (shape.Kind)
        {
            case ShapeKind.DoorSingle:
                BuildDoor(shape, parts, w, h, double_: false);
                break;
            case ShapeKind.DoorDouble:
                BuildDoor(shape, parts, w, h, double_: true);
                break;
            case ShapeKind.WindowSingle:
                BuildWindow(parts, w, h, doubleWindow: false);
                break;
            case ShapeKind.WindowDouble:
                BuildWindow(parts, w, h, doubleWindow: true);
                break;
            default:
                BuildOpening(parts, w, h);
                break;
        }

        var matrix = PlaceMatrix(shape, w, h);
        parts.Mask.Transform(matrix);
        parts.Leaves.Transform(matrix);
        parts.Lines.Transform(matrix);
        return parts;
    }

    /// <summary>Whether a click lands on the symbol: on the opening (also a little beside it, so a thin window is easy to hit), on a leaf or
    /// close to one of its lines. Follows rotation and flips.</summary>
    public static bool HitTest(Shape shape, double x, double y, double tolerance)
    {
        using var parts = Build(shape);
        var px = (float)x;
        var py = (float)y;
        if (parts.Mask.Contains(px, py) || parts.Leaves.Contains(px, py))
        {
            return true;
        }

        using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)Math.Max(1, tolerance * 2) };
        using var band = new SKPath();
        foreach (var path in new[] { parts.Mask, parts.Lines })
        {
            band.Reset();
            stroke.GetFillPath(path, band);
            if (band.Contains(px, py))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Where the symbol drawn in its own frame ends up: flipped, moved to X, Y and turned about the centre of its frame.</summary>
    public static SKMatrix PlaceMatrix(Shape shape, float w, float h)
    {
        var matrix = SKMatrix.CreateIdentity();
        if (shape.FlipHorizontal)
        {
            matrix = matrix.PostConcat(SKMatrix.CreateScale(-1, 1, w / 2, h / 2));
        }

        if (shape.FlipVertical)
        {
            matrix = matrix.PostConcat(SKMatrix.CreateScale(1, -1, w / 2, h / 2));
        }

        matrix = matrix.PostConcat(SKMatrix.CreateTranslation((float)shape.X, (float)shape.Y));
        if (shape.Rotation != 0)
        {
            matrix = matrix.PostConcat(SKMatrix.CreateRotationDegrees(
                (float)shape.Rotation, (float)(shape.X + w / 2), (float)(shape.Y + h / 2)));
        }

        return matrix;
    }

    public static (float W, float H) FrameSize(Shape shape) =>
        ((float)Math.Max(1, shape.Width), (float)(IsDoor(shape.Kind) ? DoorHeight(shape) : Math.Max(1, shape.Height)));

    /// <summary>The direction (a unit vector in the document) the dimensions of the symbol are placed in: to the outside of the wall, away from a
    /// door's swing, or to the room side.</summary>
    public static SKPoint DimensionAway(Shape shape)
    {
        var (w, h) = FrameSize(shape);
        var roomSide = shape.WallDimensionPlacement == WallDimensionPlacement.Inside;
        var v = PlaceMatrix(shape, w, h).MapVector(0, roomSide ? -1 : 1);
        var length = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
        return length < 1e-6f ? new SKPoint(0, 1) : new SKPoint(v.X / length, v.Y / length);
    }

    /// <summary>The dimension of the opening: its width from jamb to jamb, drawn next to the symbol - on the wall side (away from the swing)
    /// or, for <see cref="WallDimensionPlacement.Inside"/>, on the room side - at the distance <see cref="Shape.DimOffset"/>.
    /// Null when the symbol shows no dimension.</summary>
    public static DimensionLayout? LayoutDimension(Shape shape, float extraOffset = 0)
    {
        if (!IsSymbol(shape.Kind) || !shape.SymbolShowDimension)
        {
            return null;
        }

        var (w, h) = FrameSize(shape);
        var roomSide = shape.WallDimensionPlacement == WallDimensionPlacement.Inside;

        // the faces of the wall strip: the bottom of a door's frame, the whole frame of a window or an opening
        var innerFace = IsDoor(shape.Kind) ? h - (float)Math.Max(shape.WallThickness, 0.5) : 0f;
        var outerFace = h;
        var y = roomSide ? innerFace : outerFace;
        var matrix = PlaceMatrix(shape, w, h);
        var a = matrix.MapPoint(new SKPoint(0, y));
        var b = matrix.MapPoint(new SKPoint(w, y));
        var away = DimensionAway(shape);

        // LayoutAligned puts the line on the left of its first point to its second: take the order that points away from the symbol
        var normal = new SKPoint(-(b.Y - a.Y), b.X - a.X);
        var along = (float)Math.Clamp(shape.DimTextPosition ?? 0.5, 0, 1);
        var shift = (float)shape.DimTextShift;
        var text = DimensionRenderer.Format(w, shape.DimUnit, shape.DimPrecision);
        var distance = (float)Math.Max(0, shape.DimOffset) + extraOffset;
        var style = DimensionStyle.For(shape);
        return normal.X * away.X + normal.Y * away.Y >= 0
            ? DimensionRenderer.LayoutAligned(a, b, distance, style, text, along, shift)
            : DimensionRenderer.LayoutAligned(b, a, distance, style, text, 1 - along, shift);
    }

    /// <summary>The whole symbol as one path (the outline used for the selection frame and hit testing).</summary>
    public static SKPath BuildOutline(Shape shape)
    {
        using var parts = Build(shape);
        var outline = new SKPath();
        outline.AddPath(parts.Mask);
        outline.AddPath(parts.Leaves);
        outline.AddPath(parts.Lines);
        return outline;
    }

    public static void Draw(SKCanvas canvas, Shape shape, byte opacityAlpha)
    {
        using var parts = Build(shape);

        SKColor Alpha(ArgbColor color)
        {
            var sk = color.ToSkColor();
            return sk.WithAlpha((byte)(sk.Alpha * opacityAlpha / 255.0));
        }

        if (shape.FillEnabled)
        {
            using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Alpha(shape.FillColor) };
            canvas.DrawPath(parts.Mask, fill);
            canvas.DrawPath(parts.Leaves, fill);
        }

        if (shape.BorderThickness > 0)
        {
            using var stroke = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)shape.BorderThickness,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                Color = Alpha(shape.BorderColor),
            };
            canvas.DrawPath(parts.Leaves, stroke);
            canvas.DrawPath(parts.Lines, stroke);
        }

        var dimensionColor = new SKColor(0xC0, 0x39, 0x2B).WithAlpha(opacityAlpha);
        if (SymbolMeasurements.Current is { } document)
        {
            // the width and the distances to the wall corners (the symbol finds its wall in the document being drawn)
            foreach (var dimension in SymbolMeasurements.Layout(document, shape))
            {
                DimensionRenderer.DrawLayout(canvas, dimension.Layout, dimensionColor);
            }
        }
        else if (LayoutDimension(shape) is { } width)
        {
            DimensionRenderer.DrawLayout(canvas, width, dimensionColor);
        }
    }

    // ----- the three drawings, in the symbol's own frame (0, 0) to (w, h); the wall strip is at the bottom for a door -----------------

    private static void BuildDoor(Shape shape, Parts parts, float w, float h, bool double_)
    {
        var wall = (float)Math.Max(shape.WallThickness, 0.5);
        var hingeY = h - wall;
        var angle = (float)Math.Clamp(shape.DoorOpenAngle, 0, 120);

        // the opening: the wall is hidden between the jambs (a little beyond the faces so its outline is hidden too)
        var overshoot = (float)Math.Max(0.5, shape.BorderThickness / 2);
        parts.Mask.AddRect(new SKRect(0, hingeY - overshoot, w, h + overshoot));
        parts.Lines.MoveTo(0, hingeY);
        parts.Lines.LineTo(0, h);
        parts.Lines.MoveTo(w, hingeY);
        parts.Lines.LineTo(w, h);

        // the leaf is as long as the opening is wide (each leaf of a double door half of it): the swing is a true circle
        var leafLength = double_ ? w / 2 : w;
        AddLeaf(parts, 0, hingeY, leafLength, leafLength, angle, mirror: false, w);
        if (double_)
        {
            AddLeaf(parts, w, hingeY, leafLength, leafLength, angle, mirror: true, w);
        }
    }

    /// <summary>One leaf hinged at (hingeX, hingeY): a thin panel and its swing arc, an ellipse quarter of radii (<paramref name="rx"/>,
    /// <paramref name="ry"/>), a circle for a door placed with a click. A mirrored leaf is hinged on the right and swings to the left.</summary>
    private static void AddLeaf(Parts parts, float hingeX, float hingeY, float rx, float ry, float angleDegrees, bool mirror, float width)
    {
        var sign = mirror ? -1f : 1f;
        var radians = angleDegrees * MathF.PI / 180f;
        var tip = new SKPoint(hingeX + sign * rx * MathF.Cos(radians), hingeY - ry * MathF.Sin(radians));

        // the leaf: a thin panel from the hinge to the tip, on the side towards the opening
        var thickness = Math.Clamp(width * 0.04f, 2f, 5f);
        var dx = tip.X - hingeX;
        var dy = tip.Y - hingeY;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length > 0.5f)
        {
            var ux = dx / length;
            var uy = dy / length;
            // the side the panel is thickened to: into the opening, away from the hinge post
            var nx = -uy * sign;
            var ny = ux * sign;
            if (angleDegrees <= 0.01f)
            {
                nx = 0;
                ny = -1; // a closed leaf lies in the wall strip: thicken it upwards, into the room side
            }

            parts.Leaves.MoveTo(hingeX, hingeY);
            parts.Leaves.LineTo(tip.X, tip.Y);
            parts.Leaves.LineTo(tip.X + nx * thickness, tip.Y + ny * thickness);
            parts.Leaves.LineTo(hingeX + nx * thickness, hingeY + ny * thickness);
            parts.Leaves.Close();
        }

        // the swing arc from the tip down to the closed position at the other jamb
        if (angleDegrees > 0.5f && rx > 0.5f && ry > 0.5f)
        {
            var oval = new SKRect(hingeX - rx, hingeY - ry, hingeX + rx, hingeY + ry);
            if (mirror)
            {
                // from the closed position (180 degrees, to the left of the hinge) up to the tip
                parts.Lines.AddArc(oval, 180, angleDegrees);
            }
            else
            {
                parts.Lines.AddArc(oval, -angleDegrees, angleDegrees);
            }
        }
    }

    private static void BuildWindow(Parts parts, float w, float h, bool doubleWindow)
    {
        // the opening and the frame
        parts.Mask.AddRect(new SKRect(0, 0, w, h));
        parts.Lines.AddRect(new SKRect(0, 0, w, h));

        // two lines for the glass, a third of the way in from each face
        var first = h * 0.38f;
        var second = h * 0.62f;
        parts.Lines.MoveTo(0, first);
        parts.Lines.LineTo(w, first);
        parts.Lines.MoveTo(0, second);
        parts.Lines.LineTo(w, second);

        if (doubleWindow)
        {
            // the post between the two sashes
            parts.Lines.MoveTo(w / 2, 0);
            parts.Lines.LineTo(w / 2, h);
        }
    }

    private static void BuildOpening(Parts parts, float w, float h)
    {
        parts.Mask.AddRect(new SKRect(0, 0, w, h));
        parts.Lines.MoveTo(0, 0);
        parts.Lines.LineTo(0, h);
        parts.Lines.MoveTo(w, 0);
        parts.Lines.LineTo(w, h);
    }
}
