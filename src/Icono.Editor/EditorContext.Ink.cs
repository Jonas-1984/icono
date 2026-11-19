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

    /// <summary>Which group of the recent writing the bar offers (see <see cref="HandwritingToText.RecentGroups"/>): 0 = the group written last, 1 = the one before ...</summary>
    public int InkGroupIndex { get; private set; }

    public void NoteInk(Shape stroke)
    {
        RecentInk.Add(stroke);
        InkGroupIndex = 0; // what was just written is offered
        RecentInkChanged?.Invoke();
    }

    public void ForgetInk()
    {
        if (RecentInk.Count > 0)
        {
            RecentInk.Clear();
            InkGroupIndex = 0;
            RecentInkChanged?.Invoke();
        }
    }

    /// <summary>Offers another group of the recent writing (0 = the last one).</summary>
    public void SetInkGroup(int index)
    {
        InkGroupIndex = Math.Max(0, index);
        RecentInkChanged?.Invoke();
        RequestRedraw();
    }

    /// <summary>The recent writing changed without a new stroke (strokes were turned into text): the last group is offered again.</summary>
    public void InkListChanged()
    {
        InkGroupIndex = 0;
        RecentInkChanged?.Invoke();
        RequestRedraw();
    }

}
