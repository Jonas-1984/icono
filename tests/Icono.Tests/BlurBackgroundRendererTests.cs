using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

public class BlurBackgroundRendererTests
{
    [Fact]
    public void Render_DoesNotThrow()
    {
        var document = new Document();

        using var bitmap = new SKBitmap(64, 64);
        using var canvas = new SKCanvas(bitmap);

        var exception = Record.Exception(() => BlurBackgroundRenderer.Render(canvas, 64, 64, document));

        Assert.Null(exception);
    }

    [Fact]
    public void Render_ZeroOpacity_LeavesTheBackdropNearlyInvisible()
    {
        var document = new Document { BackgroundOpacity = 0 };

        using var bitmap = new SKBitmap(64, 64);
        using var canvas = new SKCanvas(bitmap);
        BlurBackgroundRenderer.Render(canvas, 64, 64, document);

        // The base fill's own alpha is driven entirely by BackgroundOpacity — at 0 the colored
        // panel should contribute essentially nothing over the flat backdrop clear color.
        var pixel = bitmap.GetPixel(32, 32);
        Assert.True(Math.Abs(pixel.Red - 0x2B) < 10 && Math.Abs(pixel.Green - 0x2B) < 10 && Math.Abs(pixel.Blue - 0x2B) < 10);
    }

    [Fact]
    public void Render_FullOpacityNoFade_CenterPixelReflectsChosenColor()
    {
        var document = new Document
        {
            BackgroundBlurColor = ArgbColor.FromRgb(255, 0, 0),
            BackgroundBlurAmount = 0,
            BackgroundFadeAmount = 0,
            BackgroundOpacity = 100,
        };

        using var bitmap = new SKBitmap(64, 64);
        using var canvas = new SKCanvas(bitmap);
        BlurBackgroundRenderer.Render(canvas, 64, 64, document);

        var pixel = bitmap.GetPixel(32, 32);
        Assert.True(pixel.Red > 200);
        Assert.True(pixel.Green < 50);
    }

    [Fact]
    public void Render_GradientEnabled_OppositeCornersDiffer()
    {
        var document = new Document
        {
            BackgroundBlurColor = ArgbColor.FromRgb(255, 0, 0),
            BackgroundBlurGradientEnabled = true,
            BackgroundBlurGradientColor = ArgbColor.FromRgb(0, 0, 255),
            BackgroundBlurAmount = 0,
            BackgroundFadeAmount = 0,
            BackgroundOpacity = 100,
        };

        using var bitmap = new SKBitmap(64, 64);
        using var canvas = new SKCanvas(bitmap);
        BlurBackgroundRenderer.Render(canvas, 64, 64, document);

        var topLeft = bitmap.GetPixel(1, 1);
        var bottomRight = bitmap.GetPixel(62, 62);

        Assert.NotEqual(topLeft, bottomRight);
    }
}
