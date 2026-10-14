namespace Icono.Core;

public enum BackgroundMode
{
    Transparent,
    SolidColor,
    Blur,

    /// <summary>A user-imported picture as the page background (see <see cref="Document.BackgroundImageBytes"/>).</summary>
    Image,
}
