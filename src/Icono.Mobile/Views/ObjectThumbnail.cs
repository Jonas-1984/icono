using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Mobile.Views;

/// <summary>A small picture of an object of the library, drawn by the same renderer as the drawing itself (so it always looks like what will
/// be placed), on a light tile.</summary>
public sealed class ObjectThumbnail : Control
{
    private static readonly SKColor Tile = new(0xF1, 0xF5, 0xFB);

    private readonly Document _document;

    /// <param name="shape">The object; it is moved into a little drawing of its own (pass a fresh shape, not one of the user's drawing).</param>
    public ObjectThumbnail(Shape shape)
    {
        // no measurements in a picture of 50 pixels
        shape.SymbolShowDimension = false;
        shape.SymbolShowGaps = false;
        shape.WallShowDimensions = false;

        // the drawing is exactly the object's outline with a margin, so it fills the tile
        var box = ShapeOutline.Box(shape);
        var margin = Math.Max(box.W, box.H) * 0.1 + shape.BorderThickness + (shape.Kind == ShapeKind.Wall ? shape.WallThickness : 0);
        shape.X += margin - box.X;
        shape.Y += margin - box.Y;

        _document = new Document
        {
            Width = (int)Math.Ceiling(box.W + 2 * margin),
            Height = (int)Math.Ceiling(box.H + 2 * margin),
            BackgroundMode = BackgroundMode.SolidColor,
            BackgroundColor = ArgbColor.FromRgb(Tile.Red, Tile.Green, Tile.Blue),
        };
        var layer = new Layer { Name = "Layer 1" };
        layer.Shapes.Add(shape);
        _document.Layers.Add(layer);
    }

    public override void Render(DrawingContext context) => context.Custom(new DrawOperation(new Rect(Bounds.Size), _document));

    private sealed class DrawOperation(Rect bounds, Document document) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } lease)
            {
                return;
            }

            using var api = lease.Lease();
            var canvas = api.SkCanvas;
            var tile = SKRect.Create(0, 0, (float)bounds.Width, (float)bounds.Height);
            canvas.Save();
            using (var round = new SKRoundRect(tile, 8))
            {
                canvas.ClipRoundRect(round, antialias: true);
            }

            canvas.Clear(Tile);
            var scale = (float)Math.Min(bounds.Width / document.Width, bounds.Height / document.Height);
            canvas.Translate((float)(bounds.Width - document.Width * scale) / 2, (float)(bounds.Height - document.Height * scale) / 2);
            canvas.Scale(scale);
            DocumentRenderer.Render(canvas, document, document.Width, document.Height);
            canvas.Restore();
        }
    }
}
