using Icono.Core;
using Icono.Editor;
using SkiaSharp;

namespace Icono.Tests;

/// <summary>The background of the page can be changed (colour, picture ...) and each change is one undo step.</summary>
public class DocumentBackgroundEditingTests
{
    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    [Fact]
    public void AChange_IsOneUndoStep_AndRedoBringsItBack()
    {
        var context = new EditorContext();

        DocumentBackgroundEditing.Edit(context, d =>
        {
            d.BackgroundMode = BackgroundMode.SolidColor;
            d.BackgroundColor = ArgbColor.FromRgb(10, 20, 30);
        });

        Assert.Equal(BackgroundMode.SolidColor, context.Document.BackgroundMode);
        context.Undo();
        Assert.Equal(BackgroundMode.Transparent, context.Document.BackgroundMode);
        context.Redo();
        Assert.Equal(ArgbColor.FromRgb(10, 20, 30), context.Document.BackgroundColor);
    }

    [Fact]
    public void NothingChanged_NothingToUndo()
    {
        var context = new EditorContext();
        var version = context.UndoManager.Version;

        DocumentBackgroundEditing.Edit(context, d => d.BackgroundMode = d.BackgroundMode);

        Assert.Equal(version, context.UndoManager.Version);
    }

    [Fact]
    public void ThePicture_IsKeptWithItsName_AndTakenBackByUndo()
    {
        var context = new EditorContext();
        var png = Png(20, 10);

        DocumentBackgroundEditing.Edit(context, d =>
        {
            d.BackgroundImageBytes = png;
            d.BackgroundImageName = "wall.png";
            d.BackgroundMode = BackgroundMode.Image;
        });
        context.Undo();

        Assert.Null(context.Document.BackgroundImageBytes);
        Assert.Null(context.Document.BackgroundImageName);
    }

    [Fact]
    public void ABackgroundPicture_IsMadeAtMost3000OnTheLongSide()
    {
        var big = ImageImport.ToBackgroundPng(Png(4000, 1000), out var error);

        Assert.Null(error);
        using var stored = SKBitmap.Decode(big!);
        Assert.Equal(ImageImport.MaxBackgroundSide, stored.Width);
        Assert.Equal(750, stored.Height);
    }

    [Fact]
    public void Garbage_IsNoBackground()
    {
        Assert.Null(ImageImport.ToBackgroundPng([1, 2, 3], out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
