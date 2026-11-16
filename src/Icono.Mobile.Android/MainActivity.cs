using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace Icono.Mobile.Android;

[Activity(
    Label = "icono",
    Theme = "@style/IconoTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout
                           | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
