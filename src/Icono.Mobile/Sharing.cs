namespace Icono.Mobile;

/// <summary>Handing a file to other apps (mail, messenger, cloud ...). The platform sets <see cref="ShareFile"/> (Android: the share sheet); where it
/// is not set, the app offers only saving.</summary>
public static class Sharing
{
    /// <summary>Shares (file name, MIME type, content); false when it could not be offered.</summary>
    public static Func<string, string, byte[], Task<bool>>? ShareFile { get; set; }
}
