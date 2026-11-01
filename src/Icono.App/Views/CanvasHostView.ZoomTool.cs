using System.Windows;

namespace Icono.App.Views;

/// <summary>The Zoom tool: a left click zooms in by 10 %, a right click zooms out by 10 %, and the point that was clicked stays where it is on the
/// screen (the view zooms into / out of the spot, as the zoom tool of a CAD program does).</summary>
public partial class CanvasHostView
{
    /// <summary>One click of the Zoom tool changes the zoom by this much (0.1 = 10 percentage points, as the zoom is shown in the status bar).</summary>
    private const double ZoomStepPercent = 0.1;

    /// <summary>Asks the owner of the zoom (the slider in the status bar) for a new zoom; the view's <see cref="Zoom"/> follows it.</summary>
    public event Action<double>? ZoomRequested;

    /// <summary>Changes the zoom by <paramref name="step"/> and moves the view so that the point under <paramref name="viewPoint"/> (relative to the
    /// drawing surface) stays under the pointer. The zoom is limited to what the status bar allows (10 % to 800 %).</summary>
    private void ZoomStep(double step, Point viewPoint)
    {
        if (_context is null)
        {
            return;
        }

        var before = Zoom;
        var spot = ToDocumentSpace(viewPoint);
        ZoomRequested?.Invoke(Math.Round(before + step, 2));

        var after = Zoom;
        if (Math.Abs(after - before) < 1e-9)
        {
            return;
        }

        // the page is centred in the work area, so the spot moves by (change of zoom) x (its distance from the middle of the page): the view
        // is shifted back by that much
        PanTransform.X += (after - before) * (_context.Document.Width / 2 - spot.X);
        PanTransform.Y += (after - before) * (_context.Document.TotalHeight / 2 - spot.Y);
        UpdateRulers();
    }
}
