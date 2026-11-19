using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using AndroidX.Core.Content;

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
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Sharing.ShareFile = ShareFile;
        Handwriting.Recognize = InkRecognizer.RecognizeAsync;
        Sharing.PrintPdf = (name, pdf) => PdfPrinter.Print(this, name, pdf);
    }

    protected override void OnPause()
    {
        Icono.Mobile.AppLifecycle.RaisePausing(); // the drawing that is not saved yet is copied now: Android may end the app from here on
        base.OnPause();
    }

    /// <summary>Writes the file into the app's export folder (the only folder the FileProvider hands out), then opens the share sheet with it.</summary>
    private Task<bool> ShareFile(string name, string mimeType, byte[] content)
    {
        try
        {
            var folder = new Java.IO.File(CacheDir, "exports");
            if (folder.Exists())
            {
                // only the newest export is kept there
                foreach (var old in folder.ListFiles() ?? [])
                {
                    old.Delete();
                }
            }

            folder.Mkdirs();
            var file = new Java.IO.File(folder, name);
            System.IO.File.WriteAllBytes(file.AbsolutePath, content);

            var uri = FileProvider.GetUriForFile(this, PackageName + ".fileprovider", file);
            var send = new Intent(Intent.ActionSend);
            send.SetType(mimeType);
            send.PutExtra(Intent.ExtraStream, uri);
            send.AddFlags(ActivityFlags.GrantReadUriPermission);
            StartActivity(Intent.CreateChooser(send, "Share " + name));
            return Task.FromResult(true);
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or IOException or ActivityNotFoundException)
        {
            return Task.FromResult(false);
        }
    }
}
