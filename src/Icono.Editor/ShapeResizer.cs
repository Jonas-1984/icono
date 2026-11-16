using Icono.Core;
using Icono.Rendering;

namespace Icono.Editor;

/// <summary>The eight handles round a selection frame.</summary>
public enum ResizeHandle
{
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>Where the handles of a selection sit, and how dragging them resizes or turns the shapes - shared by the Windows program
/// and the tablet app, so a shape reacts the same on both. All values are in drawing units.</summary>
public static class ShapeResizer
{
    /// <summary>The smallest width or height a resize leaves (drawing units).</summary>
    public const double MinSize = 4;

    /// <summary>The box the selection frame and its handles follow: the shape's DRAWN outline (unturned).</summary>
    public static (double X, double Y, double W, double H) Outline(Shape shape) => ShapeOutline.Box(shape);

    /// <summary>The union of the outlines of several shapes (turns ignored: the frame of a multi-selection is always upright).</summary>
    public static (double X, double Y, double W, double H) GroupBounds(IReadOnlyList<Shape> shapes)
    {
        var boxes = shapes.Select(Outline).ToList();
        var minX = boxes.Min(b => b.X);
        var minY = boxes.Min(b => b.Y);
        var maxX = boxes.Max(b => b.X + b.W);
        var maxY = boxes.Max(b => b.Y + b.H);
        return (minX, minY, maxX - minX, maxY - minY);
    }

    public static bool IsCorner(ResizeHandle handle) =>
        handle is ResizeHandle.TopLeft or ResizeHandle.TopRight or ResizeHandle.BottomLeft or ResizeHandle.BottomRight;

    /// <summary>Turns (<paramref name="x"/>, <paramref name="y"/>) by <paramref name="degrees"/> clockwise about (<paramref name="cx"/>,
    /// <paramref name="cy"/>) - the same convention Skia uses to draw a turned shape.</summary>
    public static (double X, double Y) Rotate(double x, double y, double cx, double cy, double degrees)
    {
        var rad = degrees * Math.PI / 180.0;
        var (cos, sin) = (Math.Cos(rad), Math.Sin(rad));
        var (dx, dy) = (x - cx, y - cy);
        return (cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
    }

    /// <summary>The centre a shape turns about: the middle of its X/Y/Width/Height frame (not of the outline).</summary>
    public static (double X, double Y) Pivot(Shape shape) => (shape.X + shape.Width / 2, shape.Y + shape.Height / 2);

    /// <summary>The eight handles of an upright box.</summary>
    public static IEnumerable<(ResizeHandle Handle, double X, double Y)> BoxHandles((double X, double Y, double W, double H) box)
    {
        var right = box.X + box.W;
        var bottom = box.Y + box.H;
        var midX = box.X + box.W / 2;
        var midY = box.Y + box.H / 2;

        yield return (ResizeHandle.TopLeft, box.X, box.Y);
        yield return (ResizeHandle.Top, midX, box.Y);
        yield return (ResizeHandle.TopRight, right, box.Y);
        yield return (ResizeHandle.Right, right, midY);
        yield return (ResizeHandle.BottomRight, right, bottom);
        yield return (ResizeHandle.Bottom, midX, bottom);
        yield return (ResizeHandle.BottomLeft, box.X, bottom);
        yield return (ResizeHandle.Left, box.X, midY);
    }

    /// <summary>The eight handles of one shape, on its outline and turned with it.</summary>
    public static IEnumerable<(ResizeHandle Handle, double X, double Y)> Handles(Shape shape)
    {
        var (cx, cy) = Pivot(shape);
        foreach (var (handle, x, y) in BoxHandles(Outline(shape)))
        {
            if (shape.Rotation == 0)
            {
                yield return (handle, x, y);
            }
            else
            {
                var (rx, ry) = Rotate(x, y, cx, cy, shape.Rotation);
                yield return (handle, rx, ry);
            }
        }
    }

    /// <summary>The round handle that turns a shape: <paramref name="offset"/> above the middle of its top edge, turned with it.</summary>
    public static (double X, double Y) RotateHandle(Shape shape, double offset)
    {
        var box = Outline(shape);
        var (x, y) = (box.X + box.W / 2, box.Y - offset);
        if (shape.Rotation == 0)
        {
            return (x, y);
        }

        var (cx, cy) = Pivot(shape);
        return Rotate(x, y, cx, cy, shape.Rotation);
    }

    /// <summary>The turn (degrees) that puts the rotate handle of a shape under <paramref name="x"/>, <paramref name="y"/>.</summary>
    public static double RotationTowards(Shape shape, double x, double y)
    {
        var (cx, cy) = Pivot(shape);
        return Math.Atan2(y - cy, x - cx) * 180.0 / Math.PI + 90;
    }

    /// <summary>The new X/Y/Width/Height frame of ONE shape whose <paramref name="handle"/> was dragged from <paramref name="start"/> to
    /// <paramref name="point"/>. <paramref name="startOutline"/> and <paramref name="startFrame"/> are the outline and frame at the start of the
    /// drag, <paramref name="rotation"/> and <paramref name="pivot"/> the turn and its centre then (a turned shape is resized in its own
    /// frame). A corner keeps the proportions when <paramref name="keepProportions"/> is set; <paramref name="snap"/> rounds sizes to the grid.</summary>
    public static (double X, double Y, double W, double H) ResizeFrame(
        ResizeHandle handle,
        (double X, double Y, double W, double H) startOutline,
        (double X, double Y, double W, double H) startFrame,
        (double X, double Y) start,
        (double X, double Y) point,
        double rotation,
        (double X, double Y) pivot,
        bool keepProportions,
        Func<double, double>? snap = null)
    {
        snap ??= v => v;

        // Rotate both points into the shape's local (unrotated) frame, using the rotation/center frozen at gesture-start, so the
        // left/right/top/bottom math below stays axis-aligned regardless of the shape's on-screen rotation.
        var localPoint = point;
        var localStart = start;
        if (rotation != 0)
        {
            localPoint = Rotate(point.X, point.Y, pivot.X, pivot.Y, -rotation);
            localStart = Rotate(start.X, start.Y, pivot.X, pivot.Y, -rotation);
        }

        var dx = localPoint.X - localStart.X;
        var dy = localPoint.Y - localStart.Y;
        var (startX, startY, startW, startH) = startOutline;
        keepProportions = keepProportions && IsCorner(handle) && startW > 0.0001 && startH > 0.0001;
        var (newX, newY, newW, newH) = ResizeBox(handle, startOutline, dx, dy, keepProportions, snap);

        // Proportional corner drags were already snapped as a whole (snapping X/Y/H separately would break the ratio).
        var outlineX = keepProportions ? newX : snap(newX);
        var outlineY = keepProportions ? newY : snap(newY);
        var outlineW = keepProportions ? newW : snap(newW);
        var outlineH = keepProportions ? newH : snap(newH);

        // The handles sit on the drawn outline, which is inset in the shape's X/Y/Width/Height frame by a fixed fraction;
        // turn the dragged outline back into that frame.
        var (frameX, frameY, frameW, frameH) = startFrame;
        var ratioW = frameW > 0.0001 ? Math.Max(0.0001, startW / frameW) : 1;
        var ratioH = frameH > 0.0001 ? Math.Max(0.0001, startH / frameH) : 1;
        var insetX = frameW > 0.0001 ? (startX - frameX) / frameW : 0;
        var insetY = frameH > 0.0001 ? (startY - frameY) / frameH : 0;
        var newFrameW = outlineW / ratioW;
        var newFrameH = outlineH / ratioH;
        return (outlineX - insetX * newFrameW, outlineY - insetY * newFrameH, newFrameW, newFrameH);
    }

    /// <summary>Scales every shape of a multi-selection within the resized frame of the group: each keeps its place and size relative to
    /// the frame at the start (<paramref name="startRects"/> are their X/Y/Width/Height then), so the selection resizes as one unit.</summary>
    public static void ResizeGroup(
        IReadOnlyList<Shape> shapes,
        IReadOnlyList<(double X, double Y, double W, double H)> startRects,
        ResizeHandle handle,
        (double X, double Y, double W, double H) startBounds,
        (double X, double Y) start,
        (double X, double Y) point,
        bool keepProportions,
        Func<double, double>? snap = null)
    {
        snap ??= v => v;
        var (startX, startY, startW, startH) = startBounds;
        keepProportions = keepProportions && IsCorner(handle) && startW > 0.0001 && startH > 0.0001;
        var (newX, newY, newW, newH) = ResizeBox(handle, startBounds, point.X - start.X, point.Y - start.Y, keepProportions, snap);

        if (!keepProportions)
        {
            newX = snap(newX);
            newY = snap(newY);
            newW = snap(newW);
            newH = snap(newH);
        }

        var safeStartW = startW > 0.0001 ? startW : 0.0001;
        var safeStartH = startH > 0.0001 ? startH : 0.0001;

        // Ratios MUST come from each shape's FROZEN starting rect, not its current (already mutated-this-gesture) values - reading live
        // state re-derives the ratios from an already-resized rect on every move, compounding a drift.
        for (var i = 0; i < shapes.Count; i++)
        {
            var (shapeX, shapeY, shapeW, shapeH) = startRects[i];
            shapes[i].X = newX + (shapeX - startX) / safeStartW * newW;
            shapes[i].Y = newY + (shapeY - startY) / safeStartH * newH;
            shapes[i].Width = shapeW / safeStartW * newW;
            shapes[i].Height = shapeH / safeStartH * newH;
        }
    }

    /// <summary>An upright box with one handle moved by (dx, dy); a proportional corner drag scales both sides together, anchored at the
    /// opposite corner (its width snapped as a whole).</summary>
    private static (double X, double Y, double W, double H) ResizeBox(
        ResizeHandle handle, (double X, double Y, double W, double H) box, double dx, double dy, bool keepProportions, Func<double, double> snap)
    {
        var (startX, startY, startW, startH) = box;
        double newX = startX, newY = startY, newW = startW, newH = startH;

        var affectsLeft = handle is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft;
        var affectsRight = handle is ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight;
        var affectsTop = handle is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight;
        var affectsBottom = handle is ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight;

        if (affectsLeft)
        {
            newW = Math.Max(MinSize, startW - dx);
            newX = startX + (startW - newW);
        }
        else if (affectsRight)
        {
            newW = Math.Max(MinSize, startW + dx);
        }

        if (affectsTop)
        {
            newH = Math.Max(MinSize, startH - dy);
            newY = startY + (startH - newH);
        }
        else if (affectsBottom)
        {
            newH = Math.Max(MinSize, startH + dy);
        }

        if (keepProportions)
        {
            var scale = Math.Max(newW / startW, newH / startH);
            scale = Math.Max(scale, Math.Max(MinSize / startW, MinSize / startH));

            newW = startW * scale;
            if (snap(newW) is var snappedW && Math.Abs(snappedW - newW) > 0.0001 && snappedW >= MinSize)
            {
                newW = snappedW;
                scale = newW / startW;
            }

            newH = startH * scale;
            newX = affectsLeft ? startX + startW - newW : startX;
            newY = affectsTop ? startY + startH - newH : startY;
        }

        return (newX, newY, newW, newH);
    }
}
