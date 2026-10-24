using System.IO;
using System.Windows;
using System.Windows.Threading;
using Icono.App.State;
using Icono.App.Views;

namespace Icono.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly string CrashLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Icono", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // Last line of defence: an unexpected error in one command must not take the whole editor (and the user's work) down.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteLog(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteLog(args.Exception);
            args.SetObserved();
        };

        // Must run before base.OnStartup creates the StartupUri window so it is born with the saved palette.
        ThemeManager.LoadSaved();
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteLog(e.Exception);
        e.Handled = true;

        var message = $"Something unexpected went wrong:\n\n{e.Exception.Message}\n\nYour document is still open - consider saving it. Details were written to:\n{CrashLog}";
        try
        {
            ThemedMessageBox.Show(MainWindow, message, "icono", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // The themed dialog itself failed (e.g. a broken resource): fall back to the system box.
            MessageBox.Show(message, "icono", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void WriteLog(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLog)!);
            File.AppendAllText(CrashLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
