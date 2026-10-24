using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Icono.App.Views;

/// <summary>The visual frame of a themed dialog: a glass card with soft neumorphic shadows, a draggable title row with a
/// close key, and the dialog's own content below. Styled in Themes/DialogStyles.xaml.</summary>
public class DialogCard : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(DialogCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShowCloseProperty = DependencyProperty.Register(
        nameof(ShowClose), typeof(bool), typeof(DialogCard), new PropertyMetadata(true));

    static DialogCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DialogCard), new FrameworkPropertyMetadata(typeof(DialogCard)));
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public bool ShowClose
    {
        get => (bool)GetValue(ShowCloseProperty);
        set => SetValue(ShowCloseProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("PART_Header") is UIElement header)
        {
            header.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    Window.GetWindow(this)?.DragMove();
                }
            };
        }

        if (GetTemplateChild("PART_Close") is Button close)
        {
            close.Click += (_, _) => (Window.GetWindow(this) as ThemedDialogWindow)?.CancelDialog();
        }
    }
}
