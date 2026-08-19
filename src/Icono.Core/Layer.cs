using System.Collections.ObjectModel;

namespace Icono.Core;

public sealed class Layer : ObservableObject
{
    private string _name = "Layer";
    private ArgbColor _accentColor = ArgbColor.FromRgb(0x2E, 0x86, 0xFF);
    private bool _visible = true;

    public string Name { get => _name; set => SetField(ref _name, value); }
    public ArgbColor AccentColor { get => _accentColor; set => SetField(ref _accentColor, value); }
    public bool Visible { get => _visible; set => SetField(ref _visible, value); }

    /// <summary>Index 0 = front-most (drawn last / on top).</summary>
    public ObservableCollection<Shape> Shapes { get; } = new();
}
