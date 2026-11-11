using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Icono.App.Views;

/// <summary>The documents that come with icono and lie next to icono.exe: the user manual (an HTML page), the test terms, the privacy notice and
/// the third-party notices. They open with the program Windows has for them, locally - no internet is used. Only these fixed file names are ever
/// opened (never a name from a drawing or from the user), and German or English is chosen by the language of Windows.</summary>
public static class LocalDocuments
{
    public enum Kind
    {
        Manual,
        Terms,
        Privacy,
        ThirdParty,
    }

    private static bool German => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";

    /// <summary>The file name of the document in the language of Windows.</summary>
    public static string FileName(Kind kind) => kind switch
    {
        Kind.Manual => German ? "Handbuch-DE.html" : "Manual-EN.html",
        Kind.Terms => German ? "Testbedingungen.txt" : "Terms.txt",
        Kind.Privacy => German ? "Datenschutz.txt" : "Privacy.txt",
        _ => "THIRD-PARTY-NOTICES.txt",
    };

    /// <summary>Opens the document. False when it is not there (a build that was not installed) or cannot be opened.</summary>
    public static bool TryOpen(Kind kind)
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName(kind));
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
