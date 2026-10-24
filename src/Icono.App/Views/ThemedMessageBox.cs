using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Icono.App.Views;

/// <summary>Drop-in replacement for <see cref="MessageBox"/> that matches the app's theme: a glass card with a line icon,
/// the message and themed buttons. <see cref="Choose"/> additionally allows custom button captions (e.g. Save / Don't save / Cancel).</summary>
public static class ThemedMessageBox
{
    private const string InfoGlyph = "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M12 16v-4 M12 8h.01";
    private const string WarningGlyph = "M21.73 18l-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3z M12 9v4 M12 17h.01";
    private const string ErrorGlyph = "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M15 9l-6 6 M9 9l6 6";
    private const string QuestionGlyph = "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3 M12 17h.01";

    public static MessageBoxResult Show(
        Window? owner, string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var (labels, results) = buttons switch
        {
            MessageBoxButton.OKCancel => (new[] { "OK", "Cancel" }, new[] { MessageBoxResult.OK, MessageBoxResult.Cancel }),
            MessageBoxButton.YesNo => (new[] { "Yes", "No" }, new[] { MessageBoxResult.Yes, MessageBoxResult.No }),
            MessageBoxButton.YesNoCancel => (new[] { "Yes", "No", "Cancel" }, new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel }),
            _ => (new[] { "OK" }, new[] { MessageBoxResult.OK }),
        };

        var defaultIndex = Math.Max(0, Array.IndexOf(results, defaultResult));
        var cancelIndex = Array.FindIndex(results, r => r is MessageBoxResult.Cancel or MessageBoxResult.No or MessageBoxResult.OK);
        var picked = Choose(owner, title, message, labels, defaultIndex, Math.Max(0, cancelIndex), icon);
        return results[picked];
    }

    /// <summary>Shows <paramref name="message"/> with one button per label and returns the index of the chosen one.
    /// Esc / the close key / Alt+F4 return <paramref name="cancelIndex"/>.</summary>
    public static int Choose(
        Window? owner, string title, string message, IReadOnlyList<string> labels, int defaultIndex, int cancelIndex,
        MessageBoxImage icon = MessageBoxImage.None)
    {
        var dialog = new MessageDialog(title, message, labels, defaultIndex, cancelIndex, icon);
        if (owner is { IsLoaded: true, IsVisible: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
        return dialog.ChosenIndex;
    }

    private sealed class MessageDialog : ThemedDialogWindow
    {
        private readonly int _cancelIndex;

        public int ChosenIndex { get; private set; }

        public MessageDialog(string title, string message, IReadOnlyList<string> labels, int defaultIndex, int cancelIndex, MessageBoxImage icon)
        {
            _cancelIndex = cancelIndex;
            ChosenIndex = cancelIndex;
            Title = title;

            var content = new StackPanel { MinWidth = 340, MaxWidth = 460 };

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var glyph = icon switch
            {
                MessageBoxImage.Warning => WarningGlyph,
                MessageBoxImage.Error => ErrorGlyph,
                MessageBoxImage.Question => QuestionGlyph,
                MessageBoxImage.Information => InfoGlyph,
                _ => null,
            };

            if (glyph is not null)
            {
                var path = new Path
                {
                    Data = Geometry.Parse(glyph),
                    StrokeThickness = 1.5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Width = 30,
                    Height = 30,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 2, 16, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                };
                path.SetResourceReference(Shape.StrokeProperty, icon == MessageBoxImage.Error ? "AccentVioletBrush" : "AccentBlueMidBrush");
                path.SetResourceReference(UIElement.EffectProperty, "AccentGlowEffect");
                body.Children.Add(path);
            }

            var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundTextBrush");
            Grid.SetColumn(text, glyph is null ? 0 : 1);
            Grid.SetColumnSpan(text, glyph is null ? 2 : 1);
            body.Children.Add(text);
            content.Children.Add(body);

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
            for (var i = 0; i < labels.Count; i++)
            {
                var index = i;
                var button = new Button
                {
                    Content = labels[i],
                    MinWidth = 92,
                    Height = 32,
                    Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                    Padding = new Thickness(14, 0, 14, 0),
                    IsDefault = i == defaultIndex,
                    IsCancel = i == cancelIndex,
                };
                button.Click += (_, _) =>
                {
                    ChosenIndex = index;
                    DialogResult = true;
                };
                row.Children.Add(button);
            }

            content.Children.Add(row);

            var card = new DialogCard { Title = title, Content = content, ShowClose = false };
            Content = card;

            Closing += (_, _) =>
            {
                // Alt+F4 or the like: treat as the cancel button.
                if (DialogResult is null)
                {
                    ChosenIndex = _cancelIndex;
                }
            };
        }
    }
}
