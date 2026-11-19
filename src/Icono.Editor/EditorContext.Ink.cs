using Icono.Core;

namespace Icono.Editor;

/// <summary>The handwriting of the pen since the last conversion to text (see <see cref="HandwritingToText"/>).</summary>
public sealed partial class EditorContext
{
    /// <summary>The strokes written with the pen since the last conversion (or since the tool was left), in the order they were written.</summary>
    public List<Shape> RecentInk { get; } = [];

    /// <summary>Raised when strokes were added to or taken off <see cref="RecentInk"/>.</summary>
    public event Action? RecentInkChanged;

    /// <summary>Raised when a setting that is kept for the next start changed in a place that does not announce it (the helpers of the wall tool).</summary>
    public event Action? SettingsChanged;

    public void NoteSettingsChanged() => SettingsChanged?.Invoke();

    public void NoteInk(Shape stroke)
    {
        RecentInk.Add(stroke);
        RecentInkChanged?.Invoke();
    }

    public void ForgetInk()
    {
        if (RecentInk.Count > 0)
        {
            RecentInk.Clear();
            RecentInkChanged?.Invoke();
        }
    }
}
