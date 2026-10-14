namespace Icono.Core;

/// <summary>How an imported background picture is laid onto the page.</summary>
public enum BackgroundImageFit
{
    /// <summary>Scale uniformly until the page is completely covered; overflow is cropped (centered).</summary>
    Cover,

    /// <summary>Scale uniformly until the whole picture is visible; the rest of the page shows the background colour.</summary>
    Contain,

    /// <summary>Stretch to exactly the page size, ignoring the picture's aspect ratio.</summary>
    Stretch,
}
