using SkiaSharp;

namespace Icono.Mobile;

/// <summary>What a recognizer says: the text, or why there is none.</summary>
public sealed record HandwritingResult(string? Text, string? Problem);

/// <summary>Recognizing handwriting. The platform sets <see cref="Recognize"/> (Android: Google ML Kit, on the device); where it is not set, the app
/// does not offer handwriting to text.</summary>
public static class Handwriting
{
    /// <summary>Recognizes the strokes (page points) written in the language (a tag like "en-US", "de-DE"); the third value is told what is going
    /// on (the model is being loaded ...).</summary>
    public static Func<IReadOnlyList<IReadOnlyList<SKPoint>>, string, Action<string>, Task<HandwritingResult>>? Recognize { get; set; }

    /// <summary>The languages that are offered: name and language tag.</summary>
    public static readonly (string Name, string Tag)[] Languages = [("English", "en-US"), ("Deutsch", "de-DE")];

    private static string? _language;

    private static string SettingFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "icono-handwriting.txt");

    /// <summary>The language of the writing: chosen once, kept; at first the language of the tablet.</summary>
    public static string Language
    {
        get
        {
            if (_language is not null)
            {
                return _language;
            }

            try
            {
                if (File.Exists(SettingFile) && File.ReadAllText(SettingFile).Trim() is { Length: > 0 } saved && Languages.Any(l => l.Tag == saved))
                {
                    return _language = saved;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the setting cannot be read: the default is used
            }

            return _language = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de-DE" : "en-US";
        }
        set
        {
            _language = value;
            try
            {
                File.WriteAllText(SettingFile, value);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // not kept: it is asked again next time
            }
        }
    }
}
