namespace Icono.Core;

public sealed class Shape : ObservableObject
{
    private double _x;
    private double _y;
    private double _width = 80;
    private double _height = 80;
    private ArgbColor _fillColor = ArgbColor.FromRgb(0x2E, 0x86, 0xFF);
    private ArgbColor _borderColor = ArgbColor.FromRgb(0x1E, 0x1E, 0x1E);
    private double _borderThickness = 2;
    private double _cornerRadius;
    private double _opacity = 100;
    private double _blurRadius;
    private double _fadeAmount;
    private bool _shadowEnabled;
    private double _shadowDistance = 4;
    private double _shadowStrength = 40;

    public required ShapeKind Kind { get; init; }

    public double X { get => _x; set => SetField(ref _x, value); }
    public double Y { get => _y; set => SetField(ref _y, value); }
    public double Width { get => _width; set => SetField(ref _width, value); }
    public double Height { get => _height; set => SetField(ref _height, value); }

    public ArgbColor FillColor { get => _fillColor; set => SetField(ref _fillColor, value); }
    public ArgbColor BorderColor { get => _borderColor; set => SetField(ref _borderColor, value); }
    public double BorderThickness { get => _borderThickness; set => SetField(ref _borderThickness, value); }

    /// <summary>Only meaningful for <see cref="ShapeKind.RoundedRectangle"/>.</summary>
    public double CornerRadius { get => _cornerRadius; set => SetField(ref _cornerRadius, value); }

    /// <summary>0-100. Uniform alpha applied to the whole shape.</summary>
    public double Opacity { get => _opacity; set => SetField(ref _opacity, value); }

    /// <summary>Gaussian blur radius in px, 0 = none.</summary>
    public double BlurRadius { get => _blurRadius; set => SetField(ref _blurRadius, value); }

    /// <summary>
    /// 0-100. Unlike <see cref="Opacity"/> (uniform dimming), Fade is a one-directional alpha
    /// gradient: the shape stays solid at its top edge and progressively becomes transparent
    /// toward the bottom edge. The value controls how much of the shape's height that
    /// fade-out covers (0 = no fade, 100 = fades from solid top to fully invisible bottom).
    /// </summary>
    public double FadeAmount { get => _fadeAmount; set => SetField(ref _fadeAmount, value); }

    public bool ShadowEnabled { get => _shadowEnabled; set => SetField(ref _shadowEnabled, value); }

    /// <summary>Down-right offset in px, applied equally to dx and dy.</summary>
    public double ShadowDistance { get => _shadowDistance; set => SetField(ref _shadowDistance, value); }

    /// <summary>0-100. Drives both the shadow's blur amount and its opacity.</summary>
    public double ShadowStrength { get => _shadowStrength; set => SetField(ref _shadowStrength, value); }
}
