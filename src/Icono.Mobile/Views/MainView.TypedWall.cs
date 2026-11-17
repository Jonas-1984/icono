using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Icono.Editor;

namespace Icono.Mobile.Views;

/// <summary>"Length..." in the bar of a wall being drawn: type the length of the next wall and tap the way it goes (see
/// <see cref="DocumentCanvas.PlaceTypedLength"/>). The corner is placed there and the next wall starts from it - so rooms with exact lengths are
/// drawn without dragging.</summary>
public partial class MainView
{
    private const string LengthHint = "Type the length (inside length, like the number on the wall), then tap the way the wall goes.";

    private bool IsLengthOpen => LengthLayer.IsVisible;

    private void OnPathLength(object? sender, RoutedEventArgs e)
    {
        if (!Canvas.CanTypeLength)
        {
            return;
        }

        LengthEntry.Text = string.Empty;
        LengthNote.Text = LengthHint;
        LengthLayer.IsVisible = true;

        // focus after the layer is shown, so Android brings up the keyboard
        Dispatcher.UIThread.Post(() => LengthEntry.Focus(NavigationMethod.Tab), DispatcherPriority.Input);
    }

    private void OnLengthDirection(object? sender, RoutedEventArgs e)
    {
        var (dx, dy) = (sender as Button)?.Tag switch
        {
            "u" => (0.0, -1.0),
            "d" => (0.0, 1.0),
            "l" => (-1.0, 0.0),
            _ => (1.0, 0.0),
        };

        var result = Canvas.PlaceTypedLength(LengthEntry.Text, dx, dy);
        if (result == TypedPlacement.NotALength)
        {
            LengthNote.Text = "That is not a length. Type for example 250, 2.5 m or 2500 mm.";
            return;
        }

        // the next wall can be given its length at once: the box stays open while the wall goes on
        if (result == TypedPlacement.Placed && Canvas.CanTypeLength)
        {
            LengthEntry.Text = string.Empty;
            LengthNote.Text = $"Corner placed. Next wall: type its length and tap the way it goes - or Cancel to go on by hand.";
            Dispatcher.UIThread.Post(() => LengthEntry.Focus(NavigationMethod.Tab), DispatcherPriority.Input);
            return;
        }

        CloseLength();
    }

    private void OnLengthCancel(object? sender, RoutedEventArgs e) => CloseLength();

    private void CloseLength()
    {
        LengthLayer.IsVisible = false;
        Canvas.Focus();
    }
}
