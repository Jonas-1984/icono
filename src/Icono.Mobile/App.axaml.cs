using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Icono.Mobile.Views;

namespace Icono.Mobile;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        DrawingFonts.Install();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        switch (ApplicationLifetime)
        {
            // Android: one view per activity
            case IActivityApplicationLifetime activity:
                activity.MainViewFactory = () => new MainView();
                break;
            case ISingleViewApplicationLifetime single:
                single.MainView = new MainView();
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
