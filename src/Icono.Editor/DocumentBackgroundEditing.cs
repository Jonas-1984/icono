using Icono.Core;
using Icono.Editor.Undo;

namespace Icono.Editor;

/// <summary>Changing the background of the page (colour, blur backdrop, picture): one undo step for each change, shared by the tablet app (the Windows
/// program keeps its own card). The settings are kept in the <see cref="Document"/>.</summary>
public static class DocumentBackgroundEditing
{
    private sealed record State(
        BackgroundMode Mode, ArgbColor Colour, byte[]? Image, string? ImageName, BackgroundImageFit Fit, ArgbColor BlurColour, bool GradientOn,
        ArgbColor GradientColour, double Blur, double Fade, double Opacity)
    {
        public static State Of(Document d) => new(
            d.BackgroundMode, d.BackgroundColor, d.BackgroundImageBytes, d.BackgroundImageName, d.BackgroundImageFit, d.BackgroundBlurColor,
            d.BackgroundBlurGradientEnabled, d.BackgroundBlurGradientColor, d.BackgroundBlurAmount, d.BackgroundFadeAmount, d.BackgroundOpacity);

        public void ApplyTo(Document d)
        {
            d.BackgroundMode = Mode;
            d.BackgroundColor = Colour;
            d.BackgroundImageBytes = Image;
            d.BackgroundImageName = ImageName;
            d.BackgroundImageFit = Fit;
            d.BackgroundBlurColor = BlurColour;
            d.BackgroundBlurGradientEnabled = GradientOn;
            d.BackgroundBlurGradientColor = GradientColour;
            d.BackgroundBlurAmount = Blur;
            d.BackgroundFadeAmount = Fade;
            d.BackgroundOpacity = Opacity;
        }
    }

    /// <summary>Runs <paramref name="change"/> on the document and records it as one undo step (nothing when nothing changed).</summary>
    public static void Edit(EditorContext context, Action<Document> change)
    {
        var before = State.Of(context.Document);
        change(context.Document);
        var after = State.Of(context.Document);
        if (before == after)
        {
            return;
        }

        void Apply(State state)
        {
            state.ApplyTo(context.Document);
            context.RequestRedraw();
        }

        context.UndoManager.Push(UndoableAction.ForValue(() => Apply(before), () => Apply(after)));
        context.RequestRedraw();
    }
}
