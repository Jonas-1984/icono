using System.Windows;
using Icono.App.State;

namespace Icono.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Must run before base.OnStartup creates the StartupUri window so it is born with the saved palette.
        ThemeManager.LoadSaved();
        base.OnStartup(e);
    }
}
