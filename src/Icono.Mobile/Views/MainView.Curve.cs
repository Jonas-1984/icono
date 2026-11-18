using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Icono.Mobile.Views;

/// <summary>The size of a curved corner (Curve tool): a small pad with a slider and a number box opens right at the corner.</summary>
public partial class MainView
{
    private void SetUpCurvePad() => Canvas.CurveSizeRequested += (_, request) =>
    {
        // a transform (not a margin): it counts at once, the flyout is placed from the place the anchor is at this moment
        PadAnchor.RenderTransform = new Avalonia.Media.TranslateTransform(request.At.X, request.At.Y);
        var pad = new CurvePad("Corner size", "cm", request.Cm, 100);
        var flyout = new Flyout { Content = pad, Placement = PlacementMode.Bottom };
        flyout.FlyoutPresenterClasses.Add("numpad"); // see-through around the glass of the pad
        var answered = false;
        pad.Committed += cm =>
        {
            answered = true;
            flyout.Hide();
            request.Commit(cm);
        };
        pad.Cancelled += flyout.Hide;
        flyout.Closed += (_, _) =>
        {
            if (!answered)
            {
                request.Commit(null); // closed another way: nothing is changed
            }
        };
        flyout.ShowAt(PadAnchor);
    };
}
