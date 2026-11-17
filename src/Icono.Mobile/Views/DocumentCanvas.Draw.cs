using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Icono.Core;
using Icono.Editor;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Mobile.Views;

/// <summary>Drawing: the document with the shared renderer, then the selection frames and handles in screen units.</summary>
public sealed partial class DocumentCanvas
{
    private static readonly SKColor SelectionColor = new(0x5A, 0xB0, 0xFF);
    private static readonly SKColor LockedColor = new(0x90, 0x90, 0x90);
    private static readonly SKColor SnapColor = new(0xFF, 0x9F, 0x1C);
    private static readonly SKColor CrossingColor = new(0x4C, 0xD1, 0x7A);

    public override void Render(DrawingContext context)
    {
        if (_context is null)
        {
            return;
        }

        // everything the selection shows is worked out here (UI thread) in screen units; the render thread only draws it
        var frames = _context.SelectedShapes
            .Select(s => new SelectionFrame(FrameCorners(s).Select(ToScreen).ToArray(), s.Locked || LayerOf(s)?.Locked == true))
            .ToList();
        var handles = SelectionHandles().Select(h => (h.Kind, ToScreen((h.X, h.Y)))).ToList();

        (SKPoint From, SKPoint To)? stem = null;
        if (handles.FirstOrDefault(h => h.Kind == HandleKind.Rotate) is { Kind: HandleKind.Rotate } rotate
            && _context.SelectedShapes.Count == 1)
        {
            var top = ShapeResizer.Handles(_context.SelectedShapes[0]).First(h => h.Handle == ResizeHandle.Top);
            stem = (ToScreen((top.X, top.Y)), rotate.Item2);
        }

        var pathMarks = PathPointMarks().Select(ToScreen).ToList();
        (SKPoint At, string Label)? snap = _snapMark is { } mark ? (ToScreen((mark.X, mark.Y)), mark.Label) : null;

        var chrome = new Chrome(frames, handles, stem, _rotationLabel, pathMarks, snap, MarqueeForRender(), EraserForRender(), DimensionGuideForRender());
        context.Custom(new DrawOperation(new Rect(Bounds.Size), _context.Document, PendingForRender(), _zoom, _offset, chrome));
    }

    private SKPoint ToScreen((double X, double Y) p) => new((float)(p.X * _zoom + _offset.X), (float)(p.Y * _zoom + _offset.Y));

    private readonly record struct SelectionFrame(SKPoint[] Corners, bool Locked);

    private sealed record Chrome(
        IReadOnlyList<SelectionFrame> Frames,
        IReadOnlyList<(HandleKind Kind, SKPoint At)> Handles,
        (SKPoint From, SKPoint To)? Stem,
        string? Label,
        IReadOnlyList<SKPoint> PathMarks,
        (SKPoint At, string Label)? Snap,
        (SKRect Area, bool Crossing)? Marquee,
        (SKPoint At, float Radius)? Eraser,
        (SKPoint From, SKPoint To)? DimensionGuide);

    /// <summary>The corners of the frame round a shape in drawing units (turned with it), as the Windows program draws it.</summary>
    private static (double X, double Y)[] FrameCorners(Shape shape)
    {
        var box = ShapeResizer.Outline(shape);
        var pad = Math.Max(0, shape.BorderThickness / 2);
        (double X, double Y)[] corners =
        [
            (box.X - pad, box.Y - pad), (box.X + box.W + pad, box.Y - pad),
            (box.X + box.W + pad, box.Y + box.H + pad), (box.X - pad, box.Y + box.H + pad),
        ];

        if (shape.Rotation == 0)
        {
            return corners;
        }

        var (cx, cy) = ShapeResizer.Pivot(shape);
        return corners.Select(c => ShapeResizer.Rotate(c.X, c.Y, cx, cy, shape.Rotation)).ToArray();
    }

    /// <summary>Draws on the render thread with Avalonia's own Skia canvas.</summary>
    private sealed class DrawOperation(Rect bounds, Document document, Shape? pending, double zoom, Vector offset, Chrome chrome)
        : ICustomDrawOperation
    {
        private static readonly SKColor PageShadow = new(0, 0, 0, 140);

        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (lease is null)
            {
                return;
            }

            using var api = lease.Lease();
            var canvas = api.SkCanvas;
            canvas.Save();
            canvas.ClipRect(SKRect.Create(0, 0, (float)bounds.Width, (float)bounds.Height));
            canvas.Save();
            canvas.Translate((float)offset.X, (float)offset.Y);
            canvas.Scale((float)zoom);

            using (var shadow = new SKPaint
                   {
                       Color = PageShadow,
                       IsAntialias = true,
                       MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(8 / zoom)),
                   })
            {
                for (var page = 0; page < document.PageCount; page++)
                {
                    canvas.DrawRect(SKRect.Create(0, document.PageTop(page) + (float)(3 / zoom), document.Width, document.Height), shadow);
                }
            }

            DocumentRenderer.Render(canvas, document, document.Width, document.Height, pending);
            canvas.Restore();

            // frames and handles in screen units, so they keep their size at every zoom
            DrawChrome(canvas);
            canvas.Restore();
        }

        private void DrawChrome(SKCanvas canvas)
        {
            DrawDrawingMarks(canvas);

            // the dimension line the dragged one has lined up with (so two dimensions stand in one row, flush)
            if (chrome.DimensionGuide is { } guide)
            {
                using var guideLine = new SKPaint
                {
                    IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = SnapColor, PathEffect = SKPathEffect.CreateDash([7, 5], 0),
                };
                canvas.DrawLine(guide.From, guide.To, guideLine);
            }

            // the eraser: a ring of the size it reaches
            if (chrome.Eraser is { } eraser)
            {
                using var eraserRing = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = new SKColor(0xE5, 0x48, 0x4D) };
                using var tint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(0xE5, 0x48, 0x4D, 40) };
                canvas.DrawCircle(eraser.At, eraser.Radius, tint);
                canvas.DrawCircle(eraser.At, eraser.Radius, eraserRing);
            }

            // the area being drawn: blue and solid = window (what is completely inside), green and dashed = crossing (what it touches)
            if (chrome.Marquee is { } marquee)
            {
                var color = marquee.Crossing ? CrossingColor : SelectionColor;
                using var areaFill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = color.WithAlpha(45) };
                using var edge = new SKPaint
                {
                    IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = color,
                    PathEffect = marquee.Crossing ? SKPathEffect.CreateDash([6, 4], 0) : null,
                };
                canvas.DrawRect(marquee.Area, areaFill);
                canvas.DrawRect(marquee.Area, edge);
            }

            using var frame = new SKPaint
            {
                IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, PathEffect = SKPathEffect.CreateDash([6, 4], 0),
            };
            foreach (var selection in chrome.Frames)
            {
                frame.Color = selection.Locked ? LockedColor : SelectionColor;
                var c = selection.Corners;
                for (var i = 0; i < c.Length; i++)
                {
                    canvas.DrawLine(c[i], c[(i + 1) % c.Length], frame);
                }
            }

            if (chrome.Handles.Count == 0)
            {
                return;
            }

            using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White };
            using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = SelectionColor };

            if (chrome.Stem is { } stem)
            {
                canvas.DrawLine(stem.From, stem.To, ring);
            }

            foreach (var (kind, at) in chrome.Handles)
            {
                if (kind == HandleKind.Resize)
                {
                    var rect = SKRect.Create(at.X - HandleSize / 2, at.Y - HandleSize / 2, HandleSize, HandleSize);
                    canvas.DrawRoundRect(rect, 2, 2, fill);
                    canvas.DrawRoundRect(rect, 2, 2, ring);
                }
                else
                {
                    canvas.DrawCircle(at, RoundHandleRadius, fill);
                    canvas.DrawCircle(at, RoundHandleRadius, ring);
                    if (kind == HandleKind.Rotate)
                    {
                        // a small turning arrow inside the turn handle
                        canvas.DrawArc(SKRect.Create(at.X - 3.5f, at.Y - 3.5f, 7, 7), -60, 270, false, ring);
                    }
                }
            }

            if (chrome.Label is { } label && chrome.Handles.FirstOrDefault(h => h.Kind == HandleKind.Rotate) is { Kind: HandleKind.Rotate } turn)
            {
                DrawLabel(canvas, label, new SKPoint(turn.At.X + RoundHandleRadius + 8, turn.At.Y - 13));
            }
        }

        /// <summary>The placed points of a wall / polyline / dimension under way, and where the point under the finger snapped (with what to:
        /// the label sits above the finger, where it can be read).</summary>
        private void DrawDrawingMarks(SKCanvas canvas)
        {
            using var dot = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SelectionColor };
            using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = SKColors.White };
            foreach (var mark in chrome.PathMarks)
            {
                canvas.DrawCircle(mark, 5, dot);
                canvas.DrawCircle(mark, 5, ring);
            }

            if (chrome.Snap is { } snap)
            {
                using var snapRing = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, Color = SnapColor };
                canvas.DrawCircle(snap.At, 9, snapRing);
                if (!string.IsNullOrEmpty(snap.Label))
                {
                    DrawLabel(canvas, snap.Label, new SKPoint(snap.At.X + 14, snap.At.Y - 70));
                }
            }
        }

        private static void DrawLabel(SKCanvas canvas, string label, SKPoint topLeft)
        {
            using var font = new SKFont(SKTypeface.Default, 13);
            using var text = new SKPaint { IsAntialias = true, Color = SKColors.White };
            using var back = new SKPaint { IsAntialias = true, Color = new SKColor(0x14, 0x17, 0x1E, 0xE6) };
            var width = font.MeasureText(label);
            var box = SKRect.Create(topLeft.X, topLeft.Y, width + 14, 22);
            canvas.DrawRoundRect(box, 8, 8, back);
            canvas.DrawText(label, box.Left + 8, box.Top + 16, SKTextAlign.Left, font, text);
        }
    }
}
