using System.Windows.Media.Imaging;
using Icono.App.State;

namespace Icono.App.Views;

/// <summary>The logo of icono for the windows that show it (start window, About): a blue drawing, so the dark designs get the version with a brighter
/// blue (it reads on the dark window) and the light designs the original. The picture is decoded once, at the size it is shown (twice the
/// width for sharp screens), and frozen.</summary>
public static class AppLogo
{
    public static BitmapImage Load(int decodeWidth)
    {
        var file = ThemeManager.Current == ThemeMode.Dark ? "icono-dark.png" : "icono.png";
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/Assets/{file}", UriKind.Absolute);
        image.DecodePixelWidth = decodeWidth;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
