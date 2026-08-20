using System.Collections.ObjectModel;

namespace Icono.Core;

public sealed class Layer : ObservableObject
{
    private string _name = "Layer";
    private ArgbColor _accentColor = ArgbColor.FromRgb(0x2E, 0x86, 0xFF);
    private bool _visible = true;
    private bool _locked;

    public string Name { get => _name; set => SetField(ref _name, value); }
    public ArgbColor AccentColor { get => _accentColor; set => SetField(ref _accentColor, value); }
    public bool Visible { get => _visible; set => SetField(ref _visible, value); }

    /// <summary>When true, shapes in this layer can't be moved/resized/rotated/deleted (they stay
    /// visible and clickable so the user can select the layer again to unlock it).</summary>
    public bool Locked { get => _locked; set => SetField(ref _locked, value); }

    /// <summary>Index 0 = front-most (drawn last / on top).</summary>
    public ObservableCollection<Shape> Shapes { get; } = new();
}
