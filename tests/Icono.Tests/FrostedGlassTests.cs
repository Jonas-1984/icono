using Icono.Core;
using Icono.Editor;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

[Collection("Scale")]
public class FrostedGlassTests
{
    /// <summary>A red circle (x 40 - 140), and in front of it a white pane that covers x 100 - 260; the pane is half see-through.</summary>
    private static SKBitmap Render(double backdropBlur, double opacity = 45)
    {
        var document = new Document { Width = 300, Height = 200 };
        var layer = new Layer { Name = "L" };
        var pane = ShapeTemplates.NewBox(ShapeKind.Rectangle, 100, 30, ArgbColor.FromRgb(255, 255, 255));
        pane.Width = 160;
        pane.Height = 140;
        pane.Opacity = opacity;
        pane.BorderThickness = 0;
        pane.BackdropBlurRadius = backdropBlur;
        var circle = ShapeTemplates.NewBox(ShapeKind.Ellipse, 40, 50, ArgbColor.FromRgb(230, 20, 20));
        circle.Width = 100;
        circle.Height = 100;
        circle.BorderThickness = 0;
        layer.Shapes.Add(pane); // the front-most shape comes first
        layer.Shapes.Add(circle);
        document.Layers.Add(layer);

        var bitmap = new SKBitmap(300, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        DocumentRenderer.Render(canvas, document, 300, 200, forExport: true);
        return bitmap;
    }

    /// <summary>The x where the red of the circle ends along the middle line, in a picture drawn without glass blur (inside the pane, which starts at x 100).</summary>
    private static int CircleEdge(SKBitmap sharp)
    {
        for (var x = 104; x < 200; x++)
        {
            var p = sharp.GetPixel(x, 100);
            if (p.Red - p.Green < 8)
            {
                return x;
            }
        }

        throw new InvalidOperationException("the circle was not found");
    }

    [Fact]
    public void WithoutGlassBlur_TheCircleIsSharpBehindThePane()
    {
        using var bitmap = Render(backdropBlur: 0);
        var edge = CircleEdge(bitmap);

        Assert.InRange(edge, 120, 145);
        // right beside the edge: nothing red - the circle's colour does not run
        var outside = bitmap.GetPixel(edge + 1, 100);
        Assert.True(outside.Red - outside.Green < 8, $"{outside}");
        // right inside the edge: red shows through the half see-through pane
        var inside = bitmap.GetPixel(edge - 3, 100);
        Assert.True(inside.Red - inside.Green > 30, $"{inside}");
    }

    [Fact]
    public void WithGlassBlur_WhatIsBehindThePane_IsBlurred_SoTheColourRunsOverTheEdge()
    {
        using var sharp = Render(backdropBlur: 0);
        using var frosted = Render(backdropBlur: 12);
        var edge = CircleEdge(sharp);

        // 3 px outside the circle: the blur carries red there
        var before = sharp.GetPixel(edge + 3, 100);
        var after = frosted.GetPixel(edge + 3, 100);
        Assert.True(before.Green - after.Green > 8, $"outside the circle: {before} -> {after}");

        // 4 px inside the circle: the red gets weaker at its edge (mixed with the white around it)
        var insideBefore = sharp.GetPixel(edge - 4, 100);
        var insideAfter = frosted.GetPixel(edge - 4, 100);
        Assert.True(insideAfter.Green > insideBefore.Green + 5, $"inside the circle: {insideBefore} -> {insideAfter}");
    }

    [Fact]
    public void TheGlassBlur_StaysInsideThePane_AndChangesNothingOutside()
    {
        using var sharp = Render(backdropBlur: 0);
        using var frosted = Render(backdropBlur: 12);

        // x 60 and 90 lie left of the pane (it starts at x 100): the circle is drawn as it was
        Assert.Equal(sharp.GetPixel(90, 100), frosted.GetPixel(90, 100));
        Assert.Equal(sharp.GetPixel(60, 100), frosted.GetPixel(60, 100));
        Assert.Equal(sharp.GetPixel(280, 100), frosted.GetPixel(280, 100)); // right of the pane
    }

    [Fact]
    public void TheGlassBlur_IsPartOfTheUndoSnapshot_AndOfTheGlassStylePreset()
    {
        var pane = ShapeTemplates.NewBox(ShapeKind.Rectangle, 0, 0, ArgbColor.FromRgb(200, 220, 255));
        pane.Width = 200;
        pane.Height = 120;
        pane.BackdropBlurRadius = 9;

        var copy = ShapeTemplates.NewBox(ShapeKind.Rectangle, 0, 0, ArgbColor.FromRgb(0, 0, 0));
        ShapeSnapshot.Capture(pane).ApplyTo(copy);
        Assert.Equal(9, copy.BackdropBlurRadius);

        var glass = ShapeTemplates.NewBox(ShapeKind.Rectangle, 0, 0, ArgbColor.FromRgb(200, 220, 255));
        glass.Width = 200;
        glass.Height = 120;
        DesignStylePresets.Apply(glass, DesignStyleKind.Glassmorphism);
        Assert.True(glass.BackdropBlurRadius >= 6);
    }
}

