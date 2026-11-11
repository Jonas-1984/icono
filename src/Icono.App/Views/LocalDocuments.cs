using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace Icono.App.Views;

/// <summary>The documents that come with icono and lie next to icono.exe: the user manual, the test terms and the privacy notice (web pages) and the
/// third-party notices (text). They open in the web browser of the user, locally - no internet is used. Only these fixed file names are ever opened
/// (never a name from a drawing or from the user), and German or English is chosen by the language of Windows.</summary>
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
        Kind.Terms => German ? "Testbedingungen.html" : "Terms.html",
        Kind.Privacy => German ? "Datenschutz.html" : "Privacy.html",
        _ => "THIRD-PARTY-NOTICES.txt",
    };

    /// <summary>Opens the document in the web browser. False when it is not there (a build that was not installed) or cannot be opened.</summary>
    public static bool TryOpen(Kind kind)
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName(kind));
        if (!File.Exists(path))
        {
            return false;
        }

        // Windows opens a page with the program that was chosen for ".html" - for some users an editor. A document of icono is meant to be read, so it
        // goes to the default web browser; only when that cannot be found Windows decides.
        var address = new Uri(path).AbsoluteUri;
        return TryStart(DefaultBrowserStart(address)) || TryStart(new ProcessStartInfo("msedge.exe", $"\"{address}\"") { UseShellExecute = true })
            || TryStart(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static bool TryStart(ProcessStartInfo? start)
    {
        if (start is null)
        {
            return false;
        }

        try
        {
            Process.Start(start);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }

    /// <summary>How the default web browser of the user is started with an address: Windows keeps the choice (for "https") in the registry, with the
    /// command line of the browser. Null when it cannot be found (or is started in a way that needs the shell).</summary>
    private static ProcessStartInfo? DefaultBrowserStart(string address)
    {
        try
        {
            using var choice = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            if (choice?.GetValue("ProgId") is not string progId || string.IsNullOrWhiteSpace(progId))
            {
                return null;
            }

            using var command = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
            return command?.GetValue(null) is string line ? ParseCommand(line, address) : null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>A command line of the registry ("C:\...\browser.exe" --arg "%1") with the address put in place of %1.</summary>
    internal static ProcessStartInfo? ParseCommand(string line, string address)
    {
        line = line.Trim();
        string file;
        string arguments;
        if (line.StartsWith('"'))
        {
            var end = line.IndexOf('"', 1);
            if (end < 0)
            {
                return null;
            }

            file = line[1..end];
            arguments = line[(end + 1)..].Trim();
        }
        else
        {
            var space = line.IndexOf(' ');
            file = space < 0 ? line : line[..space];
            arguments = space < 0 ? string.Empty : line[(space + 1)..].Trim();
        }

        if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
        {
            return null;
        }

        arguments = arguments.Contains("%1") ? arguments.Replace("\"%1\"", "%1").Replace("%1", $"\"{address}\"") : $"{arguments} \"{address}\"".Trim();
        return new ProcessStartInfo(file, arguments) { UseShellExecute = false };
    }
}
