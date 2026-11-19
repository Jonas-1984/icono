using Avalonia.Threading;
using Icono.Editor;

namespace Icono.Mobile.Views;

/// <summary>The settings of the editor (pen, grid, snapping ...) are read when the app starts and kept in a file of the app's data folder: shortly after every change
/// and once more when the app is left or closed, so that Android closing the app without warning loses nothing (<see cref="EditorSettings"/>).</summary>
public partial class MainView
{
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "icono-settings.json");

    private DispatcherTimer? _settingsTimer;

    private void SetUpSettings()
    {
        EditorSettings.Load(_context, SettingsPath);

        _settingsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _settingsTimer.Tick += (_, _) =>
        {
            _settingsTimer.Stop();
            EditorSettings.Save(_context, SettingsPath);
        };

        void Soon(object? sender, EventArgs e)
        {
            _settingsTimer.Stop();
            _settingsTimer.Start();
        }

        _context.PropertyChanged += Soon;
        _context.PointSnap.PropertyChanged += Soon;
        _context.ObjectSnap.PropertyChanged += Soon;
        _context.SettingsChanged += () => Soon(null, EventArgs.Empty);
        AppTheme.Changed += _context.RequestRedraw; // the rulers and labels painted on the canvas take the colours of the look
    }

    /// <summary>Writes the settings at once (the app is being left or closed).</summary>
    private void SaveSettingsNow()
    {
        _settingsTimer?.Stop();
        EditorSettings.Save(_context, SettingsPath);
    }
}
