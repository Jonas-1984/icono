namespace Icono.Mobile;

/// <summary>What the platform tells the app about its life, as events of the shared UI: the Android activity raises <see cref="Pausing"/> when the app is sent to the
/// background or left (the last moment in which Android surely lets the app write a file before it may end it).</summary>
public static class AppLifecycle
{
    /// <summary>Raised when the app goes out of the foreground.</summary>
    public static event Action? Pausing;

    public static void RaisePausing() => Pausing?.Invoke();
}
