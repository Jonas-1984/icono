using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Icono.Mobile.Android;

[Application]
public class MainApplication : AvaloniaAndroidApplication<App>
{
    protected MainApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // the version of the package (ApplicationDisplayVersion in the project) for About
        try
        {
            Views.MainView.AppVersion = PackageManager?.GetPackageInfo(PackageName ?? "", 0)?.VersionName ?? "";
        }
        catch (Exception)
        {
            // About then shows no version - not worth stopping the start
        }

        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
